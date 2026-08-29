using System.Text.Json;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using SharedInfrastructure.Idempotency;
using TicketService.Application.Common.Models;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.Consumers;
using TicketService.Infrastructure.Persistence;
using TicketService.IntegrationTests.Fixtures;

namespace TicketService.IntegrationTests.Scenarios;

/// <summary>
/// BatteryService báo một cục pin tới kỳ → TicketService mở ticket bảo trì.
/// </summary>
/// <remarks>
/// <para>
/// Đây là mắt nối đã đứt khi lịch bảo trì chuyển sang tầng tài sản: BatteryService ghi nhật
/// ký kỳ nhưng không tạo việc, nên không ai được cử đi. Bộ test này giữ cho mắt nối đó không
/// đứt lần nữa.
/// </para>
/// <para>
/// Chạy trên DbContext và UnitOfWork thật để thấy được thứ mock không nói: hàng ticket có
/// thật sự nằm trong bảng sau <c>SaveChanges</c>, và truy vấn chống trùng theo (pin, hạn kỳ)
/// có thật sự khớp hàng đã ghi hay không.
/// </para>
/// </remarks>
public class MaintenanceCycleDueConsumerTests : IClassFixture<TicketApiFactory>
{
    private static readonly Guid BatteryAssetId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid CustomerId = Guid.Parse("00000000-0000-0000-0000-0000000000a2");

    private readonly TicketApiFactory _factory;

    public MaintenanceCycleDueConsumerTests(TicketApiFactory factory)
    {
        _factory = factory;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketDbContext>();
        db.Database.EnsureDeleted();
        db.Database.EnsureCreated();
    }

    [Fact]
    public async Task DueCycle_RaisesAnOpenMaintenanceTicket_LinkedToTheBattery()
    {
        var dueAtUtc = DateTime.UtcNow.AddDays(5);
        var evt = Event(dueAtUtc);

        await ConsumeAsync(evt);

        await using var db = NewDbContext();
        var ticket = await db.Tickets.AsNoTracking()
            .SingleAsync(t => t.BatteryAssetId == BatteryAssetId);

        ticket.Status.Should().Be(TicketStatusEnum.Open);
        ticket.Origin.Should().Be(TicketOriginEnum.System);
        ticket.CustomerId.Should().Be(CustomerId);
        ticket.PeriodicMaintenanceDueAtUtc.Should().BeCloseTo(dueAtUtc, TimeSpan.FromSeconds(5));
        ticket.Code.Should().NotBeNullOrWhiteSpace();

        // Priority tính từ ma trận Impact × Urgency lúc Manager triage, không gán sẵn ở đây.
        ticket.Priority.Should().BeNull();

        // Ticket phải nối tới pin, nếu không thì màn hình tài sản không thấy việc đang mở.
        (await db.TicketBatteryAssets.AsNoTracking()
            .AnyAsync(x => x.TicketId == ticket.Id && x.BatteryAssetId == BatteryAssetId))
            .Should().BeTrue();
    }

    [Fact]
    public async Task DueCycle_WritesTicketRaisedEventToTheTransactionalOutbox()
    {
        var cycleId = Guid.NewGuid();
        var dueAtUtc = DateTime.UtcNow.AddDays(5);

        await ConsumeAsync(Event(dueAtUtc, maintenanceCycleId: cycleId));

        await using var db = NewDbContext();
        var ticket = await db.Tickets.AsNoTracking()
            .SingleAsync(t => t.BatteryAssetId == BatteryAssetId);
        var message = await db.OutboxMessages.AsNoTracking()
            .SingleAsync(m => m.Type == nameof(PeriodicMaintenanceTicketRaisedEvent));
        var raised = JsonSerializer.Deserialize<PeriodicMaintenanceTicketRaisedEvent>(message.Payload);

        raised.Should().NotBeNull();
        raised!.MaintenanceCycleId.Should().Be(cycleId);
        raised.BatteryAssetId.Should().Be(BatteryAssetId);
        raised.TicketId.Should().Be(ticket.Id);
        raised.TicketCode.Should().Be(ticket.Code);
        raised.DueAtUtc.Should().BeCloseTo(dueAtUtc, TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Kỳ chưa tới hạn: hạn chót chọn giờ chính là hạn kỳ — khách có trọn khoảng còn lại.
    /// </summary>
    [Fact]
    public async Task CycleNotYetDue_TheSchedulingDeadlineIsTheDueDate()
    {
        var dueAtUtc = DateTime.UtcNow.AddDays(7);

        await ConsumeAsync(Event(dueAtUtc));

        await using var db = NewDbContext();
        var ticket = await db.Tickets.AsNoTracking().SingleAsync(t => t.BatteryAssetId == BatteryAssetId);
        ticket.PeriodicMaintenanceScheduleDeadlineAtUtc
            .Should().BeCloseTo(dueAtUtc, TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Kỳ đã quá hạn lúc mở ticket: hạn chót đếm từ bây giờ. Lấy hạn kỳ làm hạn chót ở đây sẽ
    /// cho khách một cửa sổ đã đóng từ trước, và họ không bao giờ chọn được giờ.
    /// </summary>
    [Fact]
    public async Task OverdueCycle_GivesTheCustomerAFreshWindow()
    {
        var dueAtUtc = DateTime.UtcNow.AddDays(-30);
        var windowDays = Options().Value.OverdueScheduleWindowDays;

        await ConsumeAsync(Event(dueAtUtc));

        await using var db = NewDbContext();
        var ticket = await db.Tickets.AsNoTracking().SingleAsync(t => t.BatteryAssetId == BatteryAssetId);

        ticket.PeriodicMaintenanceScheduleDeadlineAtUtc.Should().NotBeNull();
        ticket.PeriodicMaintenanceScheduleDeadlineAtUtc!.Value
            .Should().BeCloseTo(DateTime.UtcNow.AddDays(windowDays), TimeSpan.FromMinutes(1));
        ticket.PeriodicMaintenanceScheduleDeadlineAtUtc.Value
            .Should().BeAfter(DateTime.UtcNow, "cửa sổ chọn giờ phải còn mở");
    }

    /// <summary>
    /// Message giao lại là chuyện thường của RabbitMQ. Hai lần giao cùng một kỳ chỉ được một
    /// ticket — nếu không, mỗi lần worker khởi động lại là khách nhận thêm một việc trùng.
    /// </summary>
    [Fact]
    public async Task TheSameCycleDeliveredTwice_RaisesOnlyOneTicket()
    {
        var evt = Event(DateTime.UtcNow.AddDays(3));
        var inbox = new InMemoryInboxStore();

        await ConsumeAsync(evt, inbox);
        await ConsumeAsync(evt, inbox);

        await using var db = NewDbContext();
        (await db.Tickets.AsNoTracking().CountAsync(t => t.BatteryAssetId == BatteryAssetId))
            .Should().Be(1);
    }

    /// <summary>
    /// Lớp chống trùng thứ hai: cùng (pin, hạn kỳ) nhưng Id sự kiện khác — xảy ra khi bản ghi
    /// inbox đã hết hạn và sự kiện tới lại. Truy vấn theo hạn kỳ phải chặn được.
    /// </summary>
    [Fact]
    public async Task ADifferentEventForTheSameCycle_StillRaisesOnlyOneTicket()
    {
        var dueAtUtc = DateTime.UtcNow.AddDays(3);

        await ConsumeAsync(Event(dueAtUtc));
        await ConsumeAsync(Event(dueAtUtc));   // inbox riêng ⇒ qua được lớp một

        await using var db = NewDbContext();
        (await db.Tickets.AsNoTracking().CountAsync(t => t.BatteryAssetId == BatteryAssetId))
            .Should().Be(1);
    }

    /// <summary>Kỳ kế tiếp của cùng cục pin là một việc khác, phải có ticket riêng.</summary>
    [Fact]
    public async Task TheNextCycle_GetsItsOwnTicket()
    {
        var firstDue = DateTime.UtcNow.AddDays(-1);

        await ConsumeAsync(Event(firstDue, cycleNo: 3));
        await ConsumeAsync(Event(firstDue.AddMonths(6), cycleNo: 4));

        await using var db = NewDbContext();
        (await db.Tickets.AsNoTracking().CountAsync(t => t.BatteryAssetId == BatteryAssetId))
            .Should().Be(2);
    }

    // ---------- helpers ----------

    private static MaintenanceCycleDueEvent Event(
        DateTime dueAtUtc,
        int cycleNo = 2,
        Guid? maintenanceCycleId = null) =>
        new(
            BatteryAssetId,
            CustomerId,
            "SN-TEST-0001",
            maintenanceCycleId ?? Guid.NewGuid(),
            cycleNo,
            dueAtUtc,
            6);

    private TicketDbContext NewDbContext() =>
        _factory.Services.CreateScope().ServiceProvider.GetRequiredService<TicketDbContext>();

    private IOptions<PeriodicMaintenanceOptions> Options() =>
        _factory.Services.GetRequiredService<IOptions<PeriodicMaintenanceOptions>>();

    private async Task ConsumeAsync(MaintenanceCycleDueEvent evt, IInboxStore? inbox = null)
    {
        using var scope = _factory.Services.CreateScope();
        var consumer = new TicketMaintenanceCycleDueConsumer(
            scope.ServiceProvider.GetRequiredService<ITicketUnitOfWork>(),
            scope.ServiceProvider.GetRequiredService<ITicketCodeGenerator>(),
            Options(),
            inbox ?? new InMemoryInboxStore(),
            scope.ServiceProvider.GetRequiredService<IIntegrationEventOutboxWriter>(),
            NullLogger<TicketMaintenanceCycleDueConsumer>.Instance);

        await consumer.Consume(new StubConsumeContext<MaintenanceCycleDueEvent>(evt));
    }

    /// <summary>
    /// Inbox trong bộ nhớ theo đúng vòng đời ba bước của bản Redis (GH-764): giữ chỗ → chạy →
    /// chốt, nhả chỗ khi lỗi. Bản Redis thật cần Redis, thứ bộ test này không dựng.
    /// </summary>
    private sealed class InMemoryInboxStore : IInboxStore
    {
        private enum State { InProgress, Completed }

        private readonly Dictionary<string, (State State, string Token)> _entries = new();
        private readonly object _gate = new();

        private static string Key(Guid id, string consumer) => $"{consumer}:{id:N}";

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
    /// <see cref="ConsumeContext{T}"/> tối thiểu — consumer chỉ đụng Message và
    /// CancellationToken; phần còn lại của giao diện không nằm trên đường chạy của nó.
    /// </summary>
    private sealed class StubConsumeContext<TMessage> : ConsumeContext<TMessage> where TMessage : class
    {
        public StubConsumeContext(TMessage message) => Message = message;

        public TMessage Message { get; }
        public CancellationToken CancellationToken => CancellationToken.None;
        public Guid? MessageId => (Message as SharedContracts.Events.Root.IntegrationEvent)?.Id;

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
        public bool TryGetPayload<T>([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out T? payload) where T : class { payload = null; return false; }
        public T GetOrAddPayload<T>(PayloadFactory<T> payloadFactory) where T : class => payloadFactory();
        public T AddOrUpdatePayload<T>(PayloadFactory<T> addFactory, UpdatePayloadFactory<T> updateFactory) where T : class => addFactory();
        public bool HasMessageType(Type messageType) => messageType.IsAssignableFrom(typeof(TMessage));
        public bool TryGetMessage<T>([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ConsumeContext<T>? consumeContext) where T : class { consumeContext = this as ConsumeContext<T>; return consumeContext is not null; }
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
