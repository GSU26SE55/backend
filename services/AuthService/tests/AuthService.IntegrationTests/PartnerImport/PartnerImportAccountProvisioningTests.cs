using System.Diagnostics.CodeAnalysis;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using AuthService.Infrastructure.Consumers;
using AuthService.Infrastructure.Persistence;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharedContracts.Events;
using SharedInfrastructure.Idempotency;

namespace AuthService.IntegrationTests.PartnerImport;

/// <summary>
/// Cấp tài khoản khách hàng cho luồng nhập dữ liệu bên thứ ba, chạy trên PostgreSQL thật
/// (Testcontainer) với UnitOfWork và repository thật.
/// </summary>
/// <remarks>
/// <para>
/// Bản unit test đã phủ nhánh logic bằng repository giả. Bộ này phủ phần mà repository giả không
/// nói được: ràng buộc duy nhất của cột email và số điện thoại trong PostgreSQL, việc chuẩn hoá
/// email có thật sự khớp với hàng đã nằm trong bảng hay không, và tài khoản có thật sự được ghi
/// xuống đĩa sau <c>SaveChangesAsync</c> hay không.
/// </para>
/// <para>
/// Consumer được dựng bằng tay thay vì đẩy qua bus: <see cref="AuthApiFactory"/> thay MassTransit
/// bằng bus InMemory không đăng ký consumer nào, nên publish lên bus sẽ không có ai nhận. Cách này
/// vẫn giữ nguyên phần cần kiểm chứng — repository, DbContext, ràng buộc DB đều là hàng thật.
/// </para>
/// </remarks>
[Collection("Integration")]
public class PartnerImportAccountProvisioningTests
{
    private const string CustomerRoleName = "Customer";

    private readonly AuthApiFactory _factory;

    public PartnerImportAccountProvisioningTests(AuthApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Consume_NewEmail_PersistsActiveCustomerAccount_AndPublishesThreeEvents()
    {
        var customerRoleId = await EnsureCustomerRoleAsync();
        var email = UniqueEmail("new");
        var evt = Request(email, phone: UniquePhone());

        var published = await ConsumeAsync(evt);

        await using var db = NewDbContext();
        var account = await db.Users.AsNoTracking().SingleAsync(a => a.Email == email);

        // Phải Active: bản sao khách hàng bên BatteryService chỉ sinh ra khi AuthService phát
        // AccountActivatedEvent, mà đường mời (PendingVerification) không phát sự kiện đó.
        account.Status.Should().Be(AccountStatusEnum.Active);
        account.RoleId.Should().Be(customerRoleId);
        account.EmailConfirmed.Should().BeFalse();
        account.FullName.Should().Be("Cong ty Mat Troi");
        account.PasswordHash.Should().NotBeNullOrWhiteSpace();

        published.OfType<AccountActivatedEvent>().Should().ContainSingle()
            .Which.Email.Should().Be(email);
        published.OfType<SendPartnerImportWelcomeEvent>().Should().ContainSingle()
            .Which.AccountId.Should().Be(account.Id);

        var provisioned = published.OfType<PartnerCustomerProvisionedEvent>().Should().ContainSingle().Subject;
        provisioned.AccountId.Should().Be(account.Id);
        provisioned.WasExisting.Should().BeFalse();
        provisioned.FailureReason.Should().BeNull();
        provisioned.BatchId.Should().Be(evt.BatchId);
        provisioned.RowId.Should().Be(evt.RowId);
    }

    /// <summary>
    /// Email hoa/thường lẫn lộn và có khoảng trắng vẫn phải khớp hàng đã có trong bảng — nếu không,
    /// ràng buộc duy nhất của cột email sẽ ném lỗi và cả dòng import chết.
    /// </summary>
    [Fact]
    public async Task Consume_ExistingEmailInDifferentCase_LinksInsteadOfCreatingDuplicate()
    {
        var customerRoleId = await EnsureCustomerRoleAsync();
        var email = UniqueEmail("existing");

        await using (var seed = NewDbContext())
            await TestDataSeeder.SeedActiveAccountAsync(seed, email, "Password123@", customerRoleId, "Khach Cu");

        var evt = Request($"  {email.ToUpperInvariant()}  ", phone: UniquePhone());
        var published = await ConsumeAsync(evt);

        await using var db = NewDbContext();
        var accounts = await db.Users.AsNoTracking().Where(a => a.Email == email).ToListAsync();
        accounts.Should().ContainSingle("email trùng phải liên kết vào tài khoản cũ, không tạo thêm hàng");
        accounts[0].FullName.Should().Be("Khach Cu", "không được ghi đè tên của tài khoản đang dùng");

        var provisioned = published.OfType<PartnerCustomerProvisionedEvent>().Should().ContainSingle().Subject;
        provisioned.WasExisting.Should().BeTrue();
        provisioned.AccountId.Should().Be(accounts[0].Id);

        // Bản sao bên BatteryService có thể chưa từng được đồng bộ cho tài khoản cũ.
        published.OfType<AccountSyncSnapshotEvent>().Should().ContainSingle()
            .Which.Reason.Should().Be("PartnerImportLink");

        published.OfType<AccountActivatedEvent>().Should().BeEmpty();
        published.OfType<SendPartnerImportWelcomeEvent>().Should()
            .BeEmpty("tài khoản cũ đã có mật khẩu, không gửi thư chào mừng lần nữa");
    }

    /// <summary>
    /// Số điện thoại trùng chỉ được bỏ trống, không được làm hỏng cả dòng — nếu ném lỗi thì
    /// khách hàng, site và pin của dòng đó mất theo, trong khi số điện thoại là thông tin phụ.
    /// </summary>
    [Fact]
    public async Task Consume_PhoneAlreadyTakenByAnotherAccount_KeepsCustomerAndDropsPhone()
    {
        var customerRoleId = await EnsureCustomerRoleAsync();
        var rawPhone = UniquePhone();

        await using (var seed = NewDbContext())
        {
            var owner = await TestDataSeeder.SeedActiveAccountAsync(seed, UniqueEmail("owner"), "Password123@", customerRoleId);
            // Lưu đúng dạng E.164 mà ứng dụng vẫn ghi xuống. Consumer chuẩn hoá số của đối tác
            // trước khi dò trùng, nên seed số thô "09..." sẽ không bao giờ va vào nhau và bài
            // test hoá ra chẳng kiểm được gì.
            owner.PhoneNumber = PhoneNormalizer.Normalize(rawPhone);
            seed.Users.Update(owner);
            await seed.SaveChangesAsync();
        }

        var email = UniqueEmail("dupphone");
        // Đối tác bàn giao số ở dạng nội địa; phát hiện trùng phải xuyên qua bước chuẩn hoá.
        await ConsumeAsync(Request(email, phone: rawPhone));

        await using var db = NewDbContext();
        var account = await db.Users.AsNoTracking().SingleAsync(a => a.Email == email);
        account.PhoneNumber.Should().BeNull("số trùng thì bỏ trống, dòng import vẫn phải sống");
        account.Status.Should().Be(AccountStatusEnum.Active);
    }

    /// <summary>
    /// Không có vai trò Customer đang hoạt động thì báo hỏng ngay kèm lý do đọc được, không tạo
    /// tài khoản treo và không để dòng import chờ tới hết giờ.
    /// </summary>
    [Fact]
    public async Task Consume_CustomerRoleDeactivated_ReportsFailure_AndCreatesNoAccount()
    {
        await EnsureCustomerRoleAsync();
        var email = UniqueEmail("norole");

        await SetCustomerRoleStatusAsync(RoleStatusEnum.Inactive);
        try
        {
            var published = await ConsumeAsync(Request(email, phone: UniquePhone()));

            var provisioned = published.OfType<PartnerCustomerProvisionedEvent>().Should().ContainSingle().Subject;
            provisioned.AccountId.Should().Be(Guid.Empty);
            provisioned.FailureReason.Should().NotBeNullOrWhiteSpace();
            provisioned.WasExisting.Should().BeFalse();

            published.OfType<AccountActivatedEvent>().Should().BeEmpty();
            published.OfType<SendPartnerImportWelcomeEvent>().Should().BeEmpty();

            await using var db = NewDbContext();
            (await db.Users.AsNoTracking().AnyAsync(a => a.Email == email)).Should().BeFalse();
        }
        finally
        {
            await SetCustomerRoleStatusAsync(RoleStatusEnum.Active);
        }
    }

    /// <summary>
    /// Lô import chạy qua RabbitMQ nên message giao lại là chuyện thường. Giao lại cùng một event
    /// không được sinh tài khoản thứ hai.
    /// </summary>
    [Fact]
    public async Task Consume_SameEventDeliveredTwice_CreatesExactlyOneAccount()
    {
        await EnsureCustomerRoleAsync();
        var email = UniqueEmail("redelivery");
        var evt = Request(email, phone: UniquePhone());
        var inbox = new InMemoryInboxStore();

        var first = await ConsumeAsync(evt, inbox);
        var second = await ConsumeAsync(evt, inbox);

        first.OfType<AccountActivatedEvent>().Should().ContainSingle();
        second.Should().BeEmpty("lần giao thứ hai phải bị inbox chặn trước khi chạm DB");

        await using var db = NewDbContext();
        (await db.Users.AsNoTracking().CountAsync(a => a.Email == email)).Should().Be(1);
    }

    /// <summary>
    /// Hai khách trong cùng một lô phải nhận hai mật khẩu khác nhau. Mật khẩu không ai đọc, nhưng
    /// nếu chúng giống nhau thì một lần lộ là lộ cả lô.
    /// </summary>
    [Fact]
    public async Task Consume_TwoCustomersInSameBatch_GetDistinctPasswordHashes()
    {
        await EnsureCustomerRoleAsync();
        var batchId = Guid.NewGuid();
        var emailA = UniqueEmail("batch-a");
        var emailB = UniqueEmail("batch-b");

        await ConsumeAsync(Request(emailA, phone: UniquePhone(), batchId: batchId));
        await ConsumeAsync(Request(emailB, phone: UniquePhone(), batchId: batchId));

        await using var db = NewDbContext();
        var hashes = await db.Users.AsNoTracking()
            .Where(a => a.Email == emailA || a.Email == emailB)
            .Select(a => a.PasswordHash)
            .ToListAsync();

        hashes.Should().HaveCount(2);
        hashes[0].Should().NotBe(hashes[1]);
    }

    /// <summary>
    /// Số điện thoại trống là hợp lệ — đối tác thường bàn giao thiếu cột này.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Consume_BlankPhone_StoresNullWithoutFailing(string? phone)
    {
        await EnsureCustomerRoleAsync();
        var email = UniqueEmail("nophone");

        await ConsumeAsync(Request(email, phone));

        await using var db = NewDbContext();
        var account = await db.Users.AsNoTracking().SingleAsync(a => a.Email == email);
        account.PhoneNumber.Should().BeNull();
        account.Status.Should().Be(AccountStatusEnum.Active);
    }

    // ---------- helpers ----------

    private static PartnerCustomerProvisionRequestedEvent Request(
        string email, string? phone, Guid? batchId = null) =>
        new(batchId ?? Guid.NewGuid(), Guid.NewGuid(), $"KH-{Guid.NewGuid():N}"[..12],
            email, "Cong ty Mat Troi", phone);

    private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@partner.local";

    /// <summary>Số 10 chữ số duy nhất — cột số điện thoại có ràng buộc duy nhất.</summary>
    private static string UniquePhone()
        => "09" + Math.Abs(Guid.NewGuid().GetHashCode()).ToString("D8")[..8];

    private ApplicationDbContext NewDbContext() => _factory.CreateDbContext();

    /// <summary>
    /// Trả về id của vai trò Customer đang hoạt động, tạo mới nếu bảng chưa có.
    /// </summary>
    /// <remarks>
    /// Không dùng thẳng <see cref="TestDataSeeder.CustomerRoleId"/>: seeder của chính AuthService
    /// chạy lúc factory dựng lên đã nạp bộ vai trò hệ thống với id riêng, nên
    /// <c>SeedSystemRolesAsync</c> thấy bảng có dữ liệu và bỏ qua. Ghim id cố định vào test sẽ
    /// vi phạm khoá ngoại của cột role_id. Các lớp test khác trong cùng collection lại dọn sạch
    /// bảng roles, nên mỗi lần chạy phải hỏi lại DB thay vì nhớ id từ lần trước.
    /// </remarks>
    private async Task<Guid> EnsureCustomerRoleAsync()
    {
        await using var db = NewDbContext();
        var existing = await db.Roles.IgnoreQueryFilters()
            .Where(r => r.Name == CustomerRoleName && !r.IsDeleted)
            .Select(r => new { r.Id, r.Status })
            .FirstOrDefaultAsync();

        if (existing is not null)
        {
            if (existing.Status != RoleStatusEnum.Active)
                await SetCustomerRoleStatusAsync(RoleStatusEnum.Active);
            return existing.Id;
        }

        var role = new Role
        {
            Id = Guid.NewGuid(),
            Name = CustomerRoleName,
            NormalizedName = CustomerRoleName.ToUpperInvariant(),
            Status = RoleStatusEnum.Active,
            IsSystemRole = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        return role.Id;
    }

    private async Task SetCustomerRoleStatusAsync(RoleStatusEnum status)
    {
        await using var db = NewDbContext();
        var role = await db.Roles.IgnoreQueryFilters()
            .FirstAsync(r => r.Name == CustomerRoleName && !r.IsDeleted);
        role.Status = status;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Chạy consumer với dịch vụ thật trong một scope, trả về danh sách event nó đã phát.
    /// </summary>
    private async Task<IReadOnlyList<object>> ConsumeAsync(
        PartnerCustomerProvisionRequestedEvent evt, IInboxStore? inbox = null)
    {
        using var scope = _factory.Services.CreateScope();
        var producer = new CapturingMessageProducer();

        var consumer = new PartnerCustomerProvisionRequestedConsumer(
            scope.ServiceProvider.GetRequiredService<IAuthUnitOfWork>(),
            scope.ServiceProvider.GetRequiredService<IPasswordHasher>(),
            producer,
            scope.ServiceProvider.GetRequiredService<IPublisher>(),
            inbox ?? new InMemoryInboxStore(),
            NullLogger<PartnerCustomerProvisionRequestedConsumer>.Instance);

        await consumer.Consume(new StubConsumeContext<PartnerCustomerProvisionRequestedEvent>(evt));

        return producer.Published.Cast<object>().ToList();
    }

    /// <summary>
    /// Bản inbox trong bộ nhớ theo đúng vòng đời ba bước của bản Redis (GH-764): giữ chỗ → chạy →
    /// chốt, và nhả chỗ khi lỗi. Bản Redis thật cần Redis, thứ mà integration test này không dựng.
    /// </summary>
    private sealed class InMemoryInboxStore : IInboxStore
    {
        private enum State { InProgress, Completed }

        private readonly Dictionary<string, (State State, string Token)> _entries = new();
        private readonly object _gate = new();

        private static string Key(Guid messageId, string consumerName) => $"{consumerName}:{messageId:N}";

        public Task<InboxClaim> TryBeginAsync(Guid messageId, string consumerName, CancellationToken ct = default)
        {
            lock (_gate)
            {
                var key = Key(messageId, consumerName);
                if (!_entries.TryGetValue(key, out var current))
                {
                    var token = Guid.NewGuid().ToString("N");
                    _entries[key] = (State.InProgress, token);
                    return Task.FromResult(new InboxClaim(InboxClaimStatus.Claimed, token));
                }

                return Task.FromResult(current.State == State.Completed
                    ? InboxClaim.Completed
                    : InboxClaim.Busy);
            }
        }

        public Task CompleteAsync(Guid messageId, string consumerName, string token, CancellationToken ct = default)
        {
            lock (_gate)
            {
                var key = Key(messageId, consumerName);
                if (_entries.TryGetValue(key, out var current) && current.Token == token)
                    _entries[key] = (State.Completed, token);
            }

            return Task.CompletedTask;
        }

        public Task ReleaseAsync(Guid messageId, string consumerName, string token, CancellationToken ct = default)
        {
            lock (_gate)
            {
                var key = Key(messageId, consumerName);
                if (_entries.TryGetValue(key, out var current) && current.Token == token)
                    _entries.Remove(key);
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// <see cref="ConsumeContext{T}"/> tối thiểu — consumer chỉ đụng tới Message và
    /// CancellationToken, phần còn lại của giao diện không được gọi tới.
    /// </summary>
    private sealed class StubConsumeContext<TMessage> : ConsumeContext<TMessage> where TMessage : class
    {
        public StubConsumeContext(TMessage message) => Message = message;

        public TMessage Message { get; }
        public CancellationToken CancellationToken => CancellationToken.None;

        public Guid? MessageId => (Message as SharedContracts.Events.Root.IntegrationEvent)?.Id;

        // Phần còn lại của ConsumeContext không nằm trên đường chạy của consumer này.
        public Guid? RequestId => null;
        public Guid? CorrelationId => null;
        public Guid? ConversationId => null;
        public Guid? InitiatorId => null;
        public DateTime? ExpirationTime => null;
        public Uri? SourceAddress => null;
        public Uri? DestinationAddress => null;
        public Uri? ResponseAddress => null;
        public Uri? FaultAddress => null;
        public DateTime? SentTime => null;
        public Headers Headers => throw new NotSupportedException();
        public HostInfo Host => throw new NotSupportedException();
        public IEnumerable<string> SupportedMessageTypes => new[] { typeof(TMessage).FullName ?? typeof(TMessage).Name };
        public ReceiveContext ReceiveContext => throw new NotSupportedException();
        public SerializerContext SerializerContext => throw new NotSupportedException();
        public Task ConsumeCompleted => Task.CompletedTask;

        public bool HasPayloadType(Type payloadType) => false;
        public bool TryGetPayload<T>([NotNullWhen(true)] out T? payload) where T : class { payload = null; return false; }
        public T GetOrAddPayload<T>(PayloadFactory<T> payloadFactory) where T : class => payloadFactory();
        public T AddOrUpdatePayload<T>(PayloadFactory<T> addFactory, UpdatePayloadFactory<T> updateFactory) where T : class => addFactory();
        public bool HasMessageType(Type messageType) => messageType.IsAssignableFrom(typeof(TMessage));
        public bool TryGetMessage<T>([NotNullWhen(true)] out ConsumeContext<T>? consumeContext) where T : class { consumeContext = this as ConsumeContext<T>; return consumeContext is not null; }
        public void AddConsumeTask(Task task) { }
        public void Respond<T>(T message) where T : class => throw new NotSupportedException();
        public Task RespondAsync<T>(T message) where T : class => throw new NotSupportedException();
        public Task RespondAsync<T>(T message, IPipe<SendContext<T>> sendPipe) where T : class => throw new NotSupportedException();
        public Task RespondAsync<T>(T message, IPipe<SendContext> sendPipe) where T : class => throw new NotSupportedException();
        public Task RespondAsync(object message) => throw new NotSupportedException();
        public Task RespondAsync(object message, Type messageType) => throw new NotSupportedException();
        public Task RespondAsync(object message, IPipe<SendContext> sendPipe) => throw new NotSupportedException();
        public Task RespondAsync(object message, Type messageType, IPipe<SendContext> sendPipe) => throw new NotSupportedException();
        public Task RespondAsync<T>(object values) where T : class => throw new NotSupportedException();
        public Task RespondAsync<T>(object values, IPipe<SendContext<T>> sendPipe) where T : class => throw new NotSupportedException();
        public Task RespondAsync<T>(object values, IPipe<SendContext> sendPipe) where T : class => throw new NotSupportedException();
        public Task<ISendEndpoint> GetSendEndpoint(Uri address) => throw new NotSupportedException();
        public Task NotifyConsumed<T>(ConsumeContext<T> context, TimeSpan duration, string consumerType) where T : class => Task.CompletedTask;
        public Task NotifyFaulted<T>(ConsumeContext<T> context, TimeSpan duration, string consumerType, Exception exception) where T : class => Task.CompletedTask;
        public Task NotifyConsumed(TimeSpan duration, string consumerType) => Task.CompletedTask;
        public Task NotifyFaulted(TimeSpan duration, string consumerType, Exception exception) => Task.CompletedTask;
        public Task Publish<T>(T message, CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
        public Task Publish<T>(T message, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
        public Task Publish<T>(T message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
        public Task Publish(object message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish(object message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish(object message, Type messageType, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish(object message, Type messageType, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<T>(object values, CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
        public Task Publish<T>(object values, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
        public Task Publish<T>(object values, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
        public ConnectHandle ConnectPublishObserver(IPublishObserver observer) => throw new NotSupportedException();
        public ConnectHandle ConnectSendObserver(ISendObserver observer) => throw new NotSupportedException();
    }
}
