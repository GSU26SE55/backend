using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable.Moq;
using Moq;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using SharedKernels.Interfaces;
using TicketService.Application.Consumers;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.UnitTests.Consumers;

/// <summary>
/// Sprint 7 B4 (§31.7) — TicketBatteryCascadeRiskHighConsumer: auto-upgrade Priority P1 + audit.
/// </summary>
public class BatteryCascadeRiskHighConsumerTests
{
    private readonly Mock<ITicketUnitOfWork> _uow = new();
    private readonly Mock<IGenericRepository<Ticket>> _ticketRepo = new();
    private readonly Mock<IGenericRepository<SlaTimer>> _slaTimerRepo = new();
    private readonly Mock<IActivityLogger> _activityLogger = new();
    private readonly Mock<ITicketCodeGenerator> _codeGenerator = new();       // Sprint Bonus NS-13 (#657)
    private readonly Mock<IIntegrationEventOutboxWriter> _outboxWriter = new();         // Sprint Bonus NS-13 (#657)
    // Sprint Bonus NS-12 (#656) — SlaCalculator thật (pure util) để assert DueAt recompute.
    private readonly ISlaCalculator _slaCalculator = new TicketService.Infrastructure.Implements.Utils.SlaCalculator();
    private readonly Mock<TimeProvider> _timeProvider = new();

    public BatteryCascadeRiskHighConsumerTests()
    {
        _uow.SetupGet(u => u.Tickets).Returns(_ticketRepo.Object);
        _uow.SetupGet(u => u.SlaTimers).Returns(_slaTimerRepo.Object);
        _slaTimerRepo.Setup(r => r.GetAllAsync()).Returns(new List<SlaTimer>().AsQueryable().BuildMock());
        _codeGenerator.Setup(g => g.GenerateAsync()).ReturnsAsync("TKT-AUTO-001");
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _timeProvider.Setup(t => t.GetUtcNow())
            .Returns(new DateTimeOffset(2026, 8, 17, 2, 0, 0, TimeSpan.Zero));
    }

    private TicketBatteryCascadeRiskHighConsumer Build() =>
        new(_uow.Object, _activityLogger.Object, _slaCalculator, _codeGenerator.Object, _outboxWriter.Object,
            _timeProvider.Object, NullLogger<TicketBatteryCascadeRiskHighConsumer>.Instance);

    private static ConsumeContext<BatteryCascadeRiskHighEvent> Ctx(BatteryCascadeRiskHighEvent evt)
    {
        var ctx = new Mock<ConsumeContext<BatteryCascadeRiskHighEvent>>();
        ctx.SetupGet(c => c.Message).Returns(evt);
        ctx.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return ctx.Object;
    }

    private static BatteryCascadeRiskHighEvent Event(Guid assetId, Guid? relatedTicketId, decimal score = 0.8m) =>
        new(assetId, SiteId: Guid.NewGuid(), CustomerId: Guid.NewGuid(),
            AssetSerialNumber: "BAT-001", CascadeRiskScore: score,
            RelatedTicketId: relatedTicketId, DetectedAt: DateTime.UtcNow);

    [Fact]
    public async Task ActiveTicket_NotP1_UpgradesToP1_AndLogsActivity()
    {
        var assetId = Guid.NewGuid();
        var ticketId = Guid.NewGuid();
        var ticket = new Ticket
        {
            Id = ticketId,
            Code = "T-1",
            Title = "t",
            Description = "d",
            BatteryAssetId = assetId,
            Status = TicketStatusEnum.Open,
            Priority = TicketPriorityEnum.P3Normal
        };
        _ticketRepo.Setup(r => r.GetAllAsync()).Returns(new List<Ticket> { ticket }.AsQueryable().BuildMock());

        await Build().Consume(Ctx(Event(assetId, ticketId)));

        ticket.Priority.Should().Be(TicketPriorityEnum.P1Critical);
        _activityLogger.Verify(a => a.LogAsync(
            ticketId, null, ActorRoleEnum.System, It.IsAny<string>(),
            ActivityActionEnum.PriorityAssigned,
            "P3Normal", "P1Critical", It.Is<string>(s => s.Contains("CascadeRisk"))), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AlreadyP1_Skips_NoLog_NoSave()
    {
        var assetId = Guid.NewGuid();
        var ticketId = Guid.NewGuid();
        var ticket = new Ticket
        {
            Id = ticketId,
            Code = "T-1",
            Title = "t",
            Description = "d",
            BatteryAssetId = assetId,
            Status = TicketStatusEnum.Open,
            Priority = TicketPriorityEnum.P1Critical
        };
        _ticketRepo.Setup(r => r.GetAllAsync()).Returns(new List<Ticket> { ticket }.AsQueryable().BuildMock());

        await Build().Consume(Ctx(Event(assetId, ticketId)));

        _activityLogger.Verify(a => a.LogAsync(
            It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<ActorRoleEnum>(), It.IsAny<string>(),
            It.IsAny<ActivityActionEnum>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NoActiveTicket_AutoCreatesP1SystemTicket_WithTimer()
    {
        // Sprint Bonus NS-13 (#657, R2) — không có ticket active → auto-tạo P1 (Origin=System) + timer.
        var assetId = Guid.NewGuid();
        _ticketRepo.Setup(r => r.GetAllAsync()).Returns(new List<Ticket>().AsQueryable().BuildMock());
        Ticket? created = null;
        _ticketRepo.Setup(r => r.AddAsync(It.IsAny<Ticket>())).Callback<Ticket>(t => created = t).Returns(Task.CompletedTask);
        SlaTimer? timer = null;
        _slaTimerRepo.Setup(r => r.AddAsync(It.IsAny<SlaTimer>())).Callback<SlaTimer>(t => timer = t).Returns(Task.CompletedTask);

        await Build().Consume(Ctx(Event(assetId, relatedTicketId: null)));

        created.Should().NotBeNull("event High không được rơi vào hư không");
        created!.Priority.Should().Be(TicketPriorityEnum.P1Critical);
        created.Origin.Should().Be(TicketOriginEnum.System);
        created.IsIncident.Should().BeTrue();
        created.BatteryAssetId.Should().Be(assetId);
        created.Code.Should().Be("TKT-AUTO-001");
        timer.Should().NotBeNull("ticket P1 mới cần SlaTimer chạy ngay (NS-12 dependency)");
        timer!.Status.Should().Be(SlaTimerStatusEnum.Running);
        new TicketService.Infrastructure.Implements.Utils.SlaCalculator()
            .GetWorkingMinutesBetween(timer.StartedAt, timer.DueAt)
            .Should().Be(600, "P1 = 1 ngày làm việc");
        _outboxWriter.Verify(p => p.WriteAsync(It.IsAny<SharedContracts.Events.TicketCreatedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Fallback_IncidentTicket_UpgradedToP1()
    {
        // Sprint Bonus NS-13 (#657, R6) — fallback CHỌN ticket incident.
        var assetId = Guid.NewGuid();
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Code = "T-1",
            Title = "t",
            Description = "d",
            BatteryAssetId = assetId,
            Status = TicketStatusEnum.InProgress,
            Priority = TicketPriorityEnum.P2High,
            IsIncident = true
        };
        _ticketRepo.Setup(r => r.GetAllAsync()).Returns(new List<Ticket> { ticket }.AsQueryable().BuildMock());

        await Build().Consume(Ctx(Event(assetId, relatedTicketId: null)));

        ticket.Priority.Should().Be(TicketPriorityEnum.P1Critical);
        _ticketRepo.Verify(r => r.AddAsync(It.IsAny<Ticket>()), Times.Never, "đã có ticket incident → không auto-tạo");
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Fallback_IgnoresNonIncidentTicket_AutoCreatesInstead()
    {
        // Sprint Bonus NS-13 (#657, R6) — ticket bảo trì định kỳ (không incident) KHÔNG bị nâng P1 nhầm;
        // vì đó là ticket active duy nhất và không phải incident → auto-tạo ticket P1 mới.
        var assetId = Guid.NewGuid();
        var routine = new Ticket
        {
            Id = Guid.NewGuid(),
            Code = "T-MAINT",
            Title = "Bảo trì định kỳ",
            Description = "d",
            BatteryAssetId = assetId,
            Status = TicketStatusEnum.InProgress,
            Priority = TicketPriorityEnum.P3Normal,
            Origin = TicketOriginEnum.ManualByCustomer,
            IsIncident = false
        };
        _ticketRepo.Setup(r => r.GetAllAsync()).Returns(new List<Ticket> { routine }.AsQueryable().BuildMock());
        Ticket? created = null;
        _ticketRepo.Setup(r => r.AddAsync(It.IsAny<Ticket>())).Callback<Ticket>(t => created = t).Returns(Task.CompletedTask);

        await Build().Consume(Ctx(Event(assetId, relatedTicketId: null)));

        routine.Priority.Should().Be(TicketPriorityEnum.P3Normal, "ticket bảo trì KHÔNG bị nâng P1 nhầm");
        created.Should().NotBeNull("auto-tạo ticket P1 mới thay vì đụng ticket bảo trì");
        created!.Origin.Should().Be(TicketOriginEnum.System);
    }

    [Fact]
    public async Task UpgradeToP1_RecomputesSlaTimerDueAt_AndResetsWarning()
    {
        // Sprint Bonus NS-12 (#656, R1) — cascade nâng P1 phải tính lại DueAt theo budget P1 từ mốc StartedAt
        // (không đổi thì deadline vẫn của priority cũ) + reset WarningSentAt để re-đánh giá 80%.
        var assetId = Guid.NewGuid();
        var ticketId = Guid.NewGuid();
        var startedAt = new DateTime(2026, 7, 8, 9, 0, 0, DateTimeKind.Utc);
        var ticket = new Ticket
        {
            Id = ticketId,
            Code = "T-1",
            Title = "t",
            Description = "d",
            BatteryAssetId = assetId,
            Status = TicketStatusEnum.Open,
            Priority = TicketPriorityEnum.P3Normal
        };
        var timer = new SlaTimer
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            Priority = TicketPriorityEnum.P3Normal,
            StartedAt = startedAt,
            DueAt = startedAt.AddHours(72),
            OriginalDueAt = startedAt.AddHours(72),
            Status = SlaTimerStatusEnum.Running,
            WarningSentAt = startedAt.AddHours(50)
        };
        _ticketRepo.Setup(r => r.GetAllAsync()).Returns(new List<Ticket> { ticket }.AsQueryable().BuildMock());
        _slaTimerRepo.Setup(r => r.GetAllAsync()).Returns(new List<SlaTimer> { timer }.AsQueryable().BuildMock());

        await Build().Consume(Ctx(Event(assetId, ticketId)));

        timer.DueAt.Should().Be(
            new TicketService.Infrastructure.Implements.Utils.SlaCalculator()
                .CalculateDueDate(startedAt, TicketPriorityEnum.P1Critical),
            "P1 = budget 14 ngày làm việc từ mốc StartedAt");
        timer.Priority.Should().Be(TicketPriorityEnum.P1Critical);
        timer.WarningSentAt.Should().BeNull("reset để background service re-đánh giá 80% theo deadline mới");
        _slaTimerRepo.Verify(r => r.UpdateAsync(timer), Times.Once);
    }

    [Fact]
    public async Task UpgradeToP1_NoTimer_DoesNotThrow()
    {
        // Ticket chưa từng Assigned (chưa có timer) → nâng P1 vẫn chạy, chỉ không recompute timer.
        var assetId = Guid.NewGuid();
        var ticketId = Guid.NewGuid();
        var ticket = new Ticket
        {
            Id = ticketId,
            Code = "T-1",
            Title = "t",
            Description = "d",
            BatteryAssetId = assetId,
            Status = TicketStatusEnum.Open,
            Priority = TicketPriorityEnum.P3Normal
        };
        _ticketRepo.Setup(r => r.GetAllAsync()).Returns(new List<Ticket> { ticket }.AsQueryable().BuildMock());
        // _slaTimerRepo default empty (ctor)

        await Build().Consume(Ctx(Event(assetId, ticketId)));

        ticket.Priority.Should().Be(TicketPriorityEnum.P1Critical);
        _slaTimerRepo.Verify(r => r.UpdateAsync(It.IsAny<SlaTimer>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
