using Microsoft.EntityFrameworkCore;
using SmsService.Domain.Entities;
using SmsService.Domain.Enums;
using SmsService.Infrastructure.Implements.Repositories;
using SmsService.IntegrationTests.Fixtures;

namespace SmsService.IntegrationTests.Persistence;

/// <summary>
/// <see cref="SmsUnitOfWork"/> — ba phương thức giao dịch trước đây phủ 0%.
///
/// <para>Chỗ dễ hỏng nhất là khối <c>finally</c>: quên đặt lại <c>_currentTransaction = null</c>
/// thì lần <c>BeginTransactionAsync</c> sau sẽ lặng lẽ KHÔNG mở giao dịch mới (vì thấy đã có một
/// cái), và mọi thao tác sau đó chạy ngoài giao dịch — hỏng mà không có triệu chứng. Các test dưới
/// đây dùng lại cùng một instance sau commit/rollback để chốt đúng chỗ đó.</para>
///
/// <para>Bộ test này cũng là thứ đầu tiên dựng <b>model EF thật</b> của SmsService, nên nó kéo theo
/// toàn bộ <c>IEntityTypeConfiguration</c> và <c>SmsDbContext</c> vào phạm vi được chạy — trước đây
/// chúng phủ 0% chỉ vì mọi test đều mock UnitOfWork và không có ai dựng model.</para>
/// </summary>
[Collection(nameof(SmsDatabaseCollection))]
public class SmsUnitOfWorkTests : IAsyncLifetime
{
    private readonly SmsPostgresFixture _db;
    public SmsUnitOfWorkTests(SmsPostgresFixture db) => _db = db;

    public Task InitializeAsync() => _db.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static SmsMessage Msg(string phone = "0901234567") => new()
    {
        Id = Guid.NewGuid(),
        PhoneNumber = phone,
        Message = "noi dung thu",
        SourceService = "TicketService",
        CorrelationId = Guid.NewGuid(),
        Status = SmsStatus.Pending,
    };

    private async Task<int> CountAsync()
    {
        await using var db = _db.NewContext();
        return await db.SmsMessages.CountAsync();
    }

    [Fact]
    public async Task Commit_PersistsRow()
    {
        await using (var db = _db.NewContext())
        {
            var uow = new SmsUnitOfWork(db);
            await uow.BeginTransactionAsync();
            await uow.SmsMessages.AddAsync(Msg());
            await uow.CommitTransactionAsync();
        }

        (await CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Rollback_DiscardsRow()
    {
        await using (var db = _db.NewContext())
        {
            var uow = new SmsUnitOfWork(db);
            await uow.BeginTransactionAsync();
            await uow.SmsMessages.AddAsync(Msg());
            await uow.SaveChangesAsync();
            await uow.RollbackTransactionAsync();
        }

        (await CountAsync()).Should().Be(0, "rollback phải huỷ cả thay đổi đã SaveChanges bên trong giao dịch");
    }

    [Fact]
    public async Task BeginTwice_IsNoOp_AndStillCommits()
    {
        await using (var db = _db.NewContext())
        {
            var uow = new SmsUnitOfWork(db);
            await uow.BeginTransactionAsync();
            await uow.BeginTransactionAsync();
            await uow.SmsMessages.AddAsync(Msg());
            await uow.CommitTransactionAsync();
        }

        (await CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task AfterCommit_TransactionIsReset_SoNextRollbackStillWorks()
    {
        await using (var db = _db.NewContext())
        {
            var uow = new SmsUnitOfWork(db);

            await uow.BeginTransactionAsync();
            await uow.SmsMessages.AddAsync(Msg("0900000001"));
            await uow.CommitTransactionAsync();

            await uow.BeginTransactionAsync();
            await uow.SmsMessages.AddAsync(Msg("0900000002"));
            await uow.SaveChangesAsync();
            await uow.RollbackTransactionAsync();
        }

        await using var verify = _db.NewContext();
        var phones = await verify.SmsMessages.Select(x => x.PhoneNumber).ToListAsync();
        phones.Should().BeEquivalentTo(new[] { "0900000001" },
            "giao dịch thứ hai phải là giao dịch THẬT — nếu _currentTransaction không reset sau commit thì dòng thứ hai sẽ lọt");
    }

    [Fact]
    public async Task Rollback_WithoutBegin_DoesNotThrow()
    {
        await using var db = _db.NewContext();
        var uow = new SmsUnitOfWork(db);

        var act = async () => await uow.RollbackTransactionAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Commit_WithoutBegin_StillSavesChanges()
    {
        await using (var db = _db.NewContext())
        {
            var uow = new SmsUnitOfWork(db);
            await uow.SmsMessages.AddAsync(Msg());
            await uow.CommitTransactionAsync();
        }

        (await CountAsync()).Should().Be(1);
    }

    /// <summary>
    /// Commit hỏng phải <b>tự rollback rồi ném lại</b>. Ném mà không rollback là để lại giao dịch
    /// treo giữ khoá trong Postgres — kiểu lỗi chỉ lộ ra khi tải cao.
    /// Ở đây ép hỏng bằng index unique <c>ux_sms_gateway_devices_device_code</c>.
    /// </summary>
    [Fact]
    public async Task Commit_OnConstraintViolation_RollsBackWholeBatch_AndRethrows()
    {
        await using (var seed = _db.NewContext())
        {
            seed.SmsGatewayDevices.Add(NewDevice("GW-DUP"));
            await seed.SaveChangesAsync();
        }

        await using (var db = _db.NewContext())
        {
            var uow = new SmsUnitOfWork(db);
            await uow.BeginTransactionAsync();
            await uow.SmsGatewayDevices.AddAsync(NewDevice("GW-OK"));   // hợp lệ
            await uow.SmsGatewayDevices.AddAsync(NewDevice("GW-DUP"));  // đụng unique

            var act = async () => await uow.CommitTransactionAsync();
            await act.Should().ThrowAsync<DbUpdateException>();
        }

        await using var verify = _db.NewContext();
        var codes = await verify.SmsGatewayDevices.Select(x => x.DeviceCode).ToListAsync();
        codes.Should().BeEquivalentTo(new[] { "GW-DUP" },
            "commit hỏng phải huỷ TOÀN BỘ lô — không để lại dòng hợp lệ nằm mồ côi");
    }

    [Fact]
    public async Task SaveChangesAsync_ReturnsAffectedRowCount()
    {
        await using var db = _db.NewContext();
        var uow = new SmsUnitOfWork(db);

        await uow.SmsMessages.AddAsync(Msg("0900000011"));
        await uow.SmsMessages.AddAsync(Msg("0900000012"));

        (await uow.SaveChangesAsync()).Should().Be(2);
    }

    [Fact]
    public void Dispose_DisposesUnderlyingContext()
    {
        var db = _db.NewContext();
        var uow = new SmsUnitOfWork(db);

        uow.Dispose();

        var act = () => db.SmsMessages.FirstOrDefault();
        act.Should().Throw<ObjectDisposedException>();
    }

    /// <summary>
    /// Cả 5 repository phải trỏ vào cùng một DbContext. Nếu một cái vô tình được dựng trên context
    /// khác thì ghi ở repo này sẽ không nằm trong giao dịch mở ở repo kia — hỏng nguyên tắc cốt lõi
    /// của UnitOfWork mà không có triệu chứng gì cho tới lúc cần rollback.
    /// </summary>
    [Fact]
    public async Task AllRepositories_ShareTheSameTransaction()
    {
        await using (var db = _db.NewContext())
        {
            var uow = new SmsUnitOfWork(db);
            await uow.BeginTransactionAsync();

            await uow.SmsMessages.AddAsync(Msg());
            await uow.SmsGatewayDevices.AddAsync(NewDevice("GW-TX"));
            await uow.OutboxMessages.AddAsync(new OutboxMessage
            {
                Id = Guid.NewGuid(),
                EventType = "X",
                Payload = "{}",
                OccurredAt = DateTime.UtcNow,
            });
            await uow.SmsAuditOutboxes.AddAsync(new SmsAuditOutbox
            {
                Id = Guid.NewGuid(),
                EventId = Guid.NewGuid(),
                Payload = "{}",
                Status = AuditOutboxStatusEnum.Pending,
            });

            await uow.SaveChangesAsync();
            await uow.RollbackTransactionAsync();
        }

        await using var verify = _db.NewContext();
        (await verify.SmsMessages.CountAsync()).Should().Be(0);
        (await verify.SmsGatewayDevices.CountAsync()).Should().Be(0);
        (await verify.OutboxMessages.CountAsync()).Should().Be(0);
        (await verify.SmsAuditOutboxes.CountAsync()).Should().Be(0,
            "một lần rollback phải huỷ ghi của MỌI repository — nếu không, chúng không dùng chung context");
    }

    private static SmsGatewayDevice NewDevice(string code) => new()
    {
        Id = Guid.NewGuid(),
        DeviceCode = code,
        DeviceName = "May " + code,
        ApiKeyHash = "hash",
        IsActive = true,
        DailyLimit = 100,
    };
}
