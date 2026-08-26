using System.Collections.Concurrent;
using System.Text;
using BatteryService.Application.CQRS.Command.Import;
using BatteryService.Application.CQRS.Handler.Import;
using BatteryService.Application.Import;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.Infrastructure.Consumers;
using BatteryService.Infrastructure.Implements.Repositories;
using BatteryService.Infrastructure.Persistence;
using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharedContracts.Events;
using SharedContracts.Events.Root;
using SharedContracts.Interfaces;
using SharedInfrastructure.Idempotency;
using SharedInfrastructure.Persistence.Interceptors;
using SharedInfrastructure.Services;
using ImportBatchResponse = SharedContracts.Common.Responses.CommonResponse<BatteryService.Application.DTOs.Import.ImportBatchDto>;

namespace BatteryService.IntegrationTests.Application;

/// <summary>
/// Luồng nhập dữ liệu bên thứ ba chạy qua DbContext và UnitOfWork thật, nối đủ ba chặng:
/// kiểm định (không ghi gì) → ghi thật qua tiến trình nền, kể cả nhịp chờ cấp tài khoản ở
/// AuthService → hoàn tác.
/// </summary>
/// <remarks>
/// Test đơn lẻ chỉ chứng minh được từng mảnh: bộ kiểm định bắt lỗi dòng, bộ ghi tạo bản ghi,
/// bộ hoàn tác gỡ bản ghi. Cái không mảnh nào chứng minh được là chúng nối lại có chạy không —
/// vì giữa bậc khách hàng và bậc site có một khoảng chờ THẬT qua message bus, và lô phải quay
/// lại nhiều nhịp mới đi hết. Đó là thứ bộ test này canh.
/// </remarks>
public class PartnerImportFlowTests
{
    private const string CustomersCsv =
        "external_customer_code,full_name,email,phone\n" +
        "KH-001,Cong ty Solar A,a@example.com,0901234567\n";

    private const string SitesCsv =
        "external_site_code,external_customer_code,site_name,address\n" +
        "ST-001,KH-001,Nha may Long An,KCN Long An\n";

    private const string AssetsCsv =
        "external_asset_code,external_site_code,serial_number,battery_type_name\n" +
        "PIN-001,ST-001,PYL-US3000C-88A21,LFP-100\n";

    [Fact]
    public async Task ValidateThenCommit_WritesNothingUntilCommit_ThenBuildsSiteAndAsset()
    {
        await using var db = CreateDbContext();
        SeedBatteryType(db);
        await db.SaveChangesAsync();

        var world = new ImportWorld(db);

        // ── Chặng 1: kiểm định. Không một bản ghi nghiệp vụ nào được sinh ra ở bước này.
        var created = await world.CreateBatchAsync(CustomersCsv, SitesCsv, AssetsCsv);

        created.IsSuccess.Should().BeTrue();
        created.StatusCode.Should().Be(201);
        created.Data!.TotalRows.Should().Be(3);
        created.Data.ValidRows.Should().Be(3);
        created.Data.Status.Should().Be(ImportBatchStatusEnum.ReadyToCommit);

        (await db.Sites.CountAsync()).Should().Be(0);
        (await db.BatteryAssets.CountAsync()).Should().Be(0);

        var batchId = Guid.Parse(created.Data.Id);

        // ── Chặng 2: ghi thật. Nhịp đầu chỉ xin cấp tài khoản rồi dừng — đúng như thiết kế.
        var commit = await world.CommitAsync(batchId);
        commit.StatusCode.Should().Be(202);

        var firstPass = await world.AdvanceAsync(batchId);
        firstPass.Should().BeFalse("nhịp đầu mới gửi yêu cầu cấp tài khoản, lô chưa thể xong");

        var request = world.Outbox.Events.OfType<PartnerCustomerProvisionRequestedEvent>()
            .Should().ContainSingle().Subject;
        request.ExternalCustomerCode.Should().Be("KH-001");
        request.Email.Should().Be("a@example.com");

        (await db.Sites.CountAsync()).Should().Be(0, "site chưa được dựng khi tài khoản chưa có");

        // ── AuthService trả lời: tài khoản đã tạo, và bản sao khách hàng đã đồng bộ về đây.
        var accountId = Guid.NewGuid();
        await world.ProvisionAccountAsync(request, accountId);
        await world.MirrorAccountAsync(accountId, request.Email, request.FullName);

        // ── Các nhịp còn lại: site rồi tới pin, mỗi nhịp một bậc.
        await world.RunToCompletionAsync(batchId);

        var batch = await db.ImportBatches.SingleAsync(b => b.Id == batchId);
        batch.Status.Should().Be(ImportBatchStatusEnum.Completed);

        var site = await db.Sites.SingleAsync();
        site.CustomerId.Should().Be(accountId);
        site.Name.Should().Be("Nha may Long An");

        var asset = await db.BatteryAssets.SingleAsync();
        asset.SiteId.Should().Be(site.Id);
        asset.SerialNumber.Should().Be("PYL-US3000C-88A21");
    }

    [Fact]
    public async Task Revert_AfterCompletion_RemovesTheSiteAndAssetTheBatchCreated()
    {
        await using var db = CreateDbContext();
        SeedBatteryType(db);
        await db.SaveChangesAsync();

        var world = new ImportWorld(db);
        var batchId = await world.ImportEndToEndAsync(CustomersCsv, SitesCsv, AssetsCsv);

        (await db.Sites.CountAsync()).Should().Be(1);
        (await db.BatteryAssets.CountAsync()).Should().Be(1);

        var reverted = await world.RevertAsync(batchId);

        reverted.IsSuccess.Should().BeTrue();
        (await db.BatteryAssets.CountAsync(a => !a.IsDeleted)).Should().Be(0);
        (await db.Sites.CountAsync(s => !s.IsDeleted)).Should().Be(0);

        // Bản đồ liên kết phải sạch, nếu không lần nạp sau sẽ trỏ vào bản ghi đã xoá.
        (await db.ImportEntityLinks.CountAsync(l => !l.IsDeleted && l.CreatedByBatchId == batchId))
            .Should().Be(0);

        // Tài khoản khách hàng KHÔNG bị đụng tới — nó do AuthService làm chủ.
        (await db.CustomerAccounts.CountAsync(c => !c.IsDeleted)).Should().Be(1);
    }

    [Fact]
    public async Task Revert_OnABatchThatIsStillRunning_IsRefused()
    {
        await using var db = CreateDbContext();
        var world = new ImportWorld(db);

        var created = await world.CreateBatchAsync(CustomersCsv, null, null);
        var batchId = Guid.Parse(created.Data!.Id);
        await world.CommitAsync(batchId);

        var reverted = await world.RevertAsync(batchId);

        reverted.IsSuccess.Should().BeFalse();
        reverted.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Commit_WhenEveryRowIsInvalid_IsRefusedAndWritesNothing()
    {
        await using var db = CreateDbContext();
        var world = new ImportWorld(db);

        // Site trỏ tới mã khách không có trong lô → dòng duy nhất bị đánh hỏng.
        var created = await world.CreateBatchAsync(
            null,
            "external_site_code,external_customer_code,site_name\nST-001,KH-999,Nha may Ma\n",
            null);

        created.StatusCode.Should().Be(201);
        created.Data!.ValidRows.Should().Be(0);
        created.Data.InvalidRows.Should().Be(1);

        var commit = await world.CommitAsync(Guid.Parse(created.Data.Id));

        commit.IsSuccess.Should().BeFalse();
        commit.StatusCode.Should().Be(409);
        (await db.Sites.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SameFileUploadedTwice_IsRefusedBeforeAnyRowIsParsed()
    {
        await using var db = CreateDbContext();
        var world = new ImportWorld(db);

        var first = await world.CreateBatchAsync(CustomersCsv, null, null);
        first.StatusCode.Should().Be(201);

        var second = await world.CreateBatchAsync(CustomersCsv, null, null);

        second.IsSuccess.Should().BeFalse();
        second.StatusCode.Should().Be(409);
        (await db.ImportBatches.CountAsync()).Should().Be(1);
    }

    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Gom mọi mảnh của luồng nhập lại thành một thế giới chạy được.</summary>
    private sealed class ImportWorld
    {
        private readonly ApplicationDbContext _db;
        private readonly UnitOfWork _unitOfWork;
        private readonly ImportRowValidator _validator;
        private readonly IOptions<ImportOptions> _options;
        private readonly InboxStore _inbox = new();

        public RecordingOutbox Outbox { get; } = new();

        public ImportWorld(ApplicationDbContext db)
        {
            _db = db;
            _unitOfWork = new UnitOfWork(db);
            _options = Options.Create(new ImportOptions());
            _validator = new ImportRowValidator(_options);
        }

        public Task<ImportBatchResponse> CreateBatchAsync(string? customers, string? sites, string? assets)
            => new CreateImportBatchCommandHandler(_unitOfWork, new CsvImportFileParser(), _validator, _options)
                .Handle(new CreateImportBatchCommand
                {
                    CustomersCsv = customers is null ? null : Encoding.UTF8.GetBytes(customers),
                    SitesCsv = sites is null ? null : Encoding.UTF8.GetBytes(sites),
                    AssetsCsv = assets is null ? null : Encoding.UTF8.GetBytes(assets),
                    FileName = "handover.csv"
                }, CancellationToken.None);

        public Task<ImportBatchResponse> CommitAsync(Guid batchId)
            => new CommitImportBatchCommandHandler(_unitOfWork, Mock.Of<MediatR.IPublisher>())
                .Handle(new CommitImportBatchCommand { Id = batchId }, CancellationToken.None);

        public Task<ImportBatchResponse> RevertAsync(Guid batchId)
            => new RevertImportBatchCommandHandler(_unitOfWork, Mock.Of<MediatR.IPublisher>())
                .Handle(new RevertImportBatchCommand { Id = batchId }, CancellationToken.None);

        public Task<bool> AdvanceAsync(Guid batchId)
            => new ImportCommitService(
                _unitOfWork, _validator, new BatteryTypeResolver(_unitOfWork), Outbox, _options,
                NullLogger<ImportCommitService>.Instance)
                .AdvanceAsync(batchId, CancellationToken.None);

        /// <summary>Chạy tiếp cho tới khi lô kết thúc, có chặn vòng lặp vô hạn.</summary>
        public async Task RunToCompletionAsync(Guid batchId)
        {
            for (var pass = 0; pass < 10; pass++)
            {
                if (await AdvanceAsync(batchId))
                    return;
            }

            throw new InvalidOperationException("Lô không kết thúc sau 10 nhịp.");
        }

        /// <summary>AuthService trả kết quả cấp tài khoản về.</summary>
        public async Task ProvisionAccountAsync(PartnerCustomerProvisionRequestedEvent request, Guid accountId)
        {
            var evt = new PartnerCustomerProvisionedEvent(
                request.BatchId, request.RowId, request.ExternalCustomerCode, accountId, false, null);

            await Consume(new PartnerCustomerProvisionedConsumer(
                _unitOfWork, _inbox, NullLogger<PartnerCustomerProvisionedConsumer>.Instance), evt);
        }

        /// <summary>Bản sao tài khoản đồng bộ về BatteryService qua AccountActivatedEvent.</summary>
        public async Task MirrorAccountAsync(Guid accountId, string email, string fullName)
        {
            var evt = new AccountActivatedEvent(
                accountId, email, fullName, null, "Customer", "partner-import");

            await Consume(new BatteryAccountActivatedConsumer(_unitOfWork, _inbox), evt);
        }

        /// <summary>Nạp trọn một lô từ đầu tới lúc hoàn tất và trả về định danh lô.</summary>
        public async Task<Guid> ImportEndToEndAsync(string customers, string sites, string assets)
        {
            var created = await CreateBatchAsync(customers, sites, assets);
            var batchId = Guid.Parse(created.Data!.Id);

            await CommitAsync(batchId);
            await AdvanceAsync(batchId);

            var request = Outbox.Events.OfType<PartnerCustomerProvisionRequestedEvent>().Single();
            var accountId = Guid.NewGuid();
            await ProvisionAccountAsync(request, accountId);
            await MirrorAccountAsync(accountId, request.Email, request.FullName);
            await RunToCompletionAsync(batchId);

            return batchId;
        }

        private static Task Consume<TEvent>(IConsumer<TEvent> consumer, TEvent evt) where TEvent : class
        {
            var context = new Mock<ConsumeContext<TEvent>>();
            context.SetupGet(c => c.Message).Returns(evt);
            context.SetupGet(c => c.MessageId).Returns(Guid.NewGuid());
            context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
            return consumer.Consume(context.Object);
        }
    }

    /// <summary>Giữ lại sự kiện đã ghi ra outbox để test đọc được nội dung.</summary>
    private sealed class RecordingOutbox : IIntegrationEventOutboxWriter
    {
        private readonly ConcurrentQueue<IntegrationEvent> _events = new();

        public IReadOnlyCollection<IntegrationEvent> Events => _events.ToArray();

        public Task WriteAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
            where TEvent : IntegrationEvent
        {
            _events.Enqueue(@event);
            return Task.CompletedTask;
        }
    }

    private sealed class InboxStore : IInboxStore
    {
        private readonly ConcurrentDictionary<string, (bool Completed, string Token)> _entries = new();

        private static string Key(Guid messageId, string consumerName) => $"{consumerName}:{messageId}";

        public Task<InboxClaim> TryBeginAsync(Guid messageId, string consumerName, CancellationToken cancellationToken = default)
        {
            var token = Guid.NewGuid().ToString("N");
            if (_entries.TryAdd(Key(messageId, consumerName), (false, token)))
                return Task.FromResult(new InboxClaim(InboxClaimStatus.Claimed, token));

            return Task.FromResult(_entries[Key(messageId, consumerName)].Completed
                ? InboxClaim.Completed
                : InboxClaim.Busy);
        }

        public Task CompleteAsync(Guid messageId, string consumerName, string token, CancellationToken cancellationToken = default)
        {
            var key = Key(messageId, consumerName);
            if (_entries.TryGetValue(key, out var entry) && entry.Token == token)
                _entries[key] = (true, token);

            return Task.CompletedTask;
        }

        public Task ReleaseAsync(Guid messageId, string consumerName, string token, CancellationToken cancellationToken = default)
        {
            var key = Key(messageId, consumerName);
            if (_entries.TryGetValue(key, out var entry) && entry.Token == token)
                _entries.TryRemove(key, out _);

            return Task.CompletedTask;
        }
    }

    private static void SeedBatteryType(ApplicationDbContext db)
    {
        db.BatteryTypes.Add(new BatteryType
        {
            Id = Guid.NewGuid(),
            Name = "LFP-100",
            NominalCapacityAh = 100,
            NominalVoltage = 12,
            Chemistry = BatteryChemistryEnum.LiFePO4,
            MaxCycleCount = 3000
        });
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"partner-import-integration-{Guid.NewGuid()}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new ApplicationDbContext(options, new AuditableEntityInterceptor(new CurrentUserService(new HttpContextAccessor())));
    }
}
