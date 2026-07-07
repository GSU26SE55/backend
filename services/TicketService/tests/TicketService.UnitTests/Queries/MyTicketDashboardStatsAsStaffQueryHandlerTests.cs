using TicketService.Application.CQRS.Handler.Ticket;
using TicketService.Application.CQRS.Query.Ticket;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Queries;

public class MyTicketDashboardStatsAsStaffQueryHandlerTests
{
    private readonly Mock<ITicketCurrentUserService> _mockCurrentUserService = new();

    private static Ticket MakeTicket(
        Guid? staffId,
        TicketStatusEnum status = TicketStatusEnum.InProgress,
        SlaTimer? slaTimer = null,
        DateTime? createdAt = null) => new()
        {
            Id = Guid.NewGuid(),
            Code = "T-001",
            BatteryAssetId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            AssignedStaffId = staffId,
            Title = "Test",
            Description = "desc",
            Category = TicketCategoryEnum.Other,
            Priority = TicketPriorityEnum.P3Normal,
            Status = status,
            Origin = TicketOriginEnum.ManualByCustomer,
            SlaTimer = slaTimer,
            CreatedAt = createdAt ?? DateTime.UtcNow
        };

    /// <summary>Timer Running còn ~91% thời gian — healthy.</summary>
    private static SlaTimer HealthyTimer() => new()
    {
        Id = Guid.NewGuid(),
        Status = SlaTimerStatusEnum.Running,
        StartedAt = DateTime.UtcNow.AddMinutes(-10),
        DueAt = DateTime.UtcNow.AddMinutes(100)
    };

    /// <summary>Timer Running còn ~9% thời gian — sắp breach (≤25%).</summary>
    private static SlaTimer NearBreachTimer() => new()
    {
        Id = Guid.NewGuid(),
        Status = SlaTimerStatusEnum.Running,
        StartedAt = DateTime.UtcNow.AddMinutes(-100),
        DueAt = DateTime.UtcNow.AddMinutes(10)
    };

    private static SlaTimer Timer(SlaTimerStatusEnum status) => new()
    {
        Id = Guid.NewGuid(),
        Status = status,
        StartedAt = DateTime.UtcNow.AddHours(-2),
        DueAt = DateTime.UtcNow.AddHours(2)
    };

    private MyTicketDashboardStatsAsStaffQueryHandler MakeHandler(params Ticket[] tickets)
    {
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: tickets);
        return new MyTicketDashboardStatsAsStaffQueryHandler(uow.Object, _mockCurrentUserService.Object);
    }

    [Fact]
    public async Task Handle_NoUser_Returns401()
    {
        _mockCurrentUserService.Setup(s => s.UserId).Returns((string?)null);

        var result = await MakeHandler().Handle(new MyTicketDashboardStatsAsStaffQuery(), default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task Handle_ScopesToCurrentStaffOnly()
    {
        var myId = Guid.NewGuid();
        _mockCurrentUserService.Setup(s => s.UserId).Returns(myId.ToString());

        var result = await MakeHandler(
            MakeTicket(myId, TicketStatusEnum.InProgress),
            MakeTicket(myId, TicketStatusEnum.Resolved),
            MakeTicket(Guid.NewGuid(), TicketStatusEnum.InProgress) // staff khác — không tính
        ).Handle(new MyTicketDashboardStatsAsStaffQuery(), default);

        result.IsSuccess.Should().BeTrue();
        result.Data!.OpenCount.Should().Be(1);
        result.Data.ResolvedCount.Should().Be(1);
        result.Data.CountByStatus["InProgress"].Should().Be(1);
    }

    [Fact]
    public async Task Handle_SlaRisk_BucketsMonitoredTimers()
    {
        var myId = Guid.NewGuid();
        _mockCurrentUserService.Setup(s => s.UserId).Returns(myId.ToString());

        var result = await MakeHandler(
            MakeTicket(myId, TicketStatusEnum.InProgress, HealthyTimer()),
            MakeTicket(myId, TicketStatusEnum.InProgress, NearBreachTimer()),
            MakeTicket(myId, TicketStatusEnum.Escalated, Timer(SlaTimerStatusEnum.Breached)),
            MakeTicket(myId, TicketStatusEnum.WaitingParts, Timer(SlaTimerStatusEnum.Paused)),
            MakeTicket(myId, TicketStatusEnum.New, HealthyTimer()), // New không thuộc nhóm monitored
            MakeTicket(myId, TicketStatusEnum.InProgress) // không có timer — không monitored
        ).Handle(new MyTicketDashboardStatsAsStaffQuery(), default);

        result.Data!.SlaMonitoredCount.Should().Be(4);
        result.Data.NearBreachCount.Should().Be(1);
        result.Data.BreachedCount.Should().Be(1);
        result.Data.PausedCount.Should().Be(1);
        result.Data.SlaRisk.Near.Should().Be(1);
        result.Data.SlaRisk.Breached.Should().Be(1);
        result.Data.SlaRisk.Healthy.Should().Be(2); // healthy running + paused
    }

    [Fact]
    public async Task Handle_ResolvedCount_ExcludesClosedRejected()
    {
        var myId = Guid.NewGuid();
        _mockCurrentUserService.Setup(s => s.UserId).Returns(myId.ToString());

        var result = await MakeHandler(
            MakeTicket(myId, TicketStatusEnum.Resolved),
            MakeTicket(myId, TicketStatusEnum.Closed),
            MakeTicket(myId, TicketStatusEnum.ClosedRejected)
        ).Handle(new MyTicketDashboardStatsAsStaffQuery(), default);

        result.Data!.ResolvedCount.Should().Be(2);
        result.Data.OpenCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_SlaSummary_CoversAllAssignedTimers()
    {
        var myId = Guid.NewGuid();
        _mockCurrentUserService.Setup(s => s.UserId).Returns(myId.ToString());

        var result = await MakeHandler(
            MakeTicket(myId, TicketStatusEnum.Closed, Timer(SlaTimerStatusEnum.Met)),
            MakeTicket(myId, TicketStatusEnum.Closed, Timer(SlaTimerStatusEnum.Met)),
            MakeTicket(myId, TicketStatusEnum.Escalated, Timer(SlaTimerStatusEnum.Breached))
        ).Handle(new MyTicketDashboardStatsAsStaffQuery(), default);

        result.Data!.Sla.Met.Should().Be(2);
        result.Data.Sla.Breached.Should().Be(1);
        result.Data.Sla.CompliancePercent.Should().BeApproximately(66.67, 0.01);
    }
}
