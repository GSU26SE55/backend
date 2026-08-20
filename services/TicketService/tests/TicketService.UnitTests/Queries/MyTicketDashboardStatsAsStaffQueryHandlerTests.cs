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
        TicketStatusEnum status = TicketStatusEnum.InProgress,
        SlaTimer? slaTimer = null,
        DateTime? createdAt = null) => new()
        {
            Id = Guid.NewGuid(),
            Code = "T-001",
            BatteryAssetId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Title = "Test",
            Description = "desc",
            Category = TicketCategoryEnum.Other,
            Priority = TicketPriorityEnum.P3Normal,
            Status = status,
            Origin = TicketOriginEnum.ManualByCustomer,
            SlaTimer = slaTimer,
            CreatedAt = createdAt ?? DateTime.UtcNow
        };

    private static TicketAssignment AssignPrimary(Guid ticketId, Guid staffId) => new()
    {
        Id = Guid.NewGuid(),
        TicketId = ticketId,
        StaffId = staffId,
        Role = AssignmentRoleEnum.PrimaryHandler
    };

    /// <summary>Timer Running còn ~83% thời gian — healthy.</summary>
    private static SlaTimer HealthyTimer() => new()
    {
        Id = Guid.NewGuid(),
        Priority = TicketPriorityEnum.P3Normal,
        Status = SlaTimerStatusEnum.Running,
        StartedAt = DateTime.UtcNow.AddMinutes(-10),
        // P3 budget = 1200 phút làm việc → còn 1000 phút ≈ 83%.
        DueAt = new TicketService.Infrastructure.Implements.Utils.SlaCalculator()
            .AddWorkingMinutes(DateTime.UtcNow, 1000)
    };

    /// <summary>Timer Running còn ~9% thời gian — sắp breach (≤25%).</summary>
    private static SlaTimer NearBreachTimer() => new()
    {
        Id = Guid.NewGuid(),
        Priority = TicketPriorityEnum.P3Normal,
        Status = SlaTimerStatusEnum.Running,
        StartedAt = DateTime.UtcNow.AddMinutes(-100),
        // P3 budget = 1200 phút làm việc → còn 100 phút ≈ 8%.
        DueAt = new TicketService.Infrastructure.Implements.Utils.SlaCalculator()
            .AddWorkingMinutes(DateTime.UtcNow, 100)
    };

    private static SlaTimer Timer(SlaTimerStatusEnum status) => new()
    {
        Id = Guid.NewGuid(),
        Priority = TicketPriorityEnum.P3Normal,
        Status = status,
        StartedAt = DateTime.UtcNow.AddHours(-2),
        DueAt = DateTime.UtcNow.AddHours(2)
    };

    private MyTicketDashboardStatsAsStaffQueryHandler MakeHandler(
        Ticket[] tickets,
        TicketAssignment[]? assignments = null)
    {
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(
            ticketSeed: tickets,
            assignmentSeed: assignments);
        return new MyTicketDashboardStatsAsStaffQueryHandler(
            uow.Object, _mockCurrentUserService.Object,
            new TicketService.Infrastructure.Implements.Utils.SlaCalculator());
    }

    [Fact]
    public async Task Handle_NoUser_Returns401()
    {
        _mockCurrentUserService.Setup(s => s.UserId).Returns((string?)null);

        var result = await MakeHandler([]).Handle(new MyTicketDashboardStatsAsStaffQuery(), default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task Handle_ScopesToCurrentStaffOnly()
    {
        var myId = Guid.NewGuid();
        _mockCurrentUserService.Setup(s => s.UserId).Returns(myId.ToString());

        var t1 = MakeTicket(TicketStatusEnum.InProgress);
        var t2 = MakeTicket(TicketStatusEnum.Completed);
        var t3 = MakeTicket(TicketStatusEnum.InProgress); // staff khác — không tính

        var result = await MakeHandler(
            [t1, t2, t3],
            [AssignPrimary(t1.Id, myId), AssignPrimary(t2.Id, myId)]
        ).Handle(new MyTicketDashboardStatsAsStaffQuery(), default);

        result.IsSuccess.Should().BeTrue();
        result.Data!.OpenCount.Should().Be(2);
        result.Data.ResolvedCount.Should().Be(1);
        result.Data.CountByStatus["InProgress"].Should().Be(1);
    }

    [Fact]
    public async Task Handle_SlaRisk_BucketsMonitoredTimers()
    {
        var myId = Guid.NewGuid();
        _mockCurrentUserService.Setup(s => s.UserId).Returns(myId.ToString());

        var t1 = MakeTicket(TicketStatusEnum.InProgress, HealthyTimer());
        var t2 = MakeTicket(TicketStatusEnum.InProgress, NearBreachTimer());
        var t3 = MakeTicket(TicketStatusEnum.ReAssign, Timer(SlaTimerStatusEnum.Breached));
        var t4 = MakeTicket(TicketStatusEnum.Pending, Timer(SlaTimerStatusEnum.Paused));
        var t5 = MakeTicket(TicketStatusEnum.Open, HealthyTimer()); // Open is not monitored.
        var t6 = MakeTicket(TicketStatusEnum.InProgress);          // không có timer — không monitored

        var result = await MakeHandler(
            [t1, t2, t3, t4, t5, t6],
            [AssignPrimary(t1.Id, myId), AssignPrimary(t2.Id, myId), AssignPrimary(t3.Id, myId),
             AssignPrimary(t4.Id, myId), AssignPrimary(t5.Id, myId), AssignPrimary(t6.Id, myId)]
        ).Handle(new MyTicketDashboardStatsAsStaffQuery(), default);

        result.Data!.SlaMonitoredCount.Should().Be(2);
        result.Data.NearBreachCount.Should().Be(1);
        result.Data.BreachedCount.Should().Be(0);
        result.Data.PausedCount.Should().Be(0);
        result.Data.SlaRisk.Near.Should().Be(1);
        result.Data.SlaRisk.Breached.Should().Be(0);
        result.Data.SlaRisk.Healthy.Should().Be(1);
    }

    [Fact]
    public async Task Handle_ResolvedCount_ExcludesClosedRejected()
    {
        var myId = Guid.NewGuid();
        _mockCurrentUserService.Setup(s => s.UserId).Returns(myId.ToString());

        var t1 = MakeTicket(TicketStatusEnum.Completed);
        var t2 = MakeTicket(TicketStatusEnum.Closed);
        var t3 = MakeTicket(TicketStatusEnum.ClosedRejected);

        var result = await MakeHandler(
            [t1, t2, t3],
            [AssignPrimary(t1.Id, myId), AssignPrimary(t2.Id, myId), AssignPrimary(t3.Id, myId)]
        ).Handle(new MyTicketDashboardStatsAsStaffQuery(), default);

        result.Data!.ResolvedCount.Should().Be(2);
        result.Data.OpenCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_SlaSummary_CoversAllAssignedTimers()
    {
        var myId = Guid.NewGuid();
        _mockCurrentUserService.Setup(s => s.UserId).Returns(myId.ToString());

        var t1 = MakeTicket(TicketStatusEnum.Closed, Timer(SlaTimerStatusEnum.Met));
        var t2 = MakeTicket(TicketStatusEnum.Closed, Timer(SlaTimerStatusEnum.Met));
        var t3 = MakeTicket(TicketStatusEnum.ReAssign, Timer(SlaTimerStatusEnum.Breached));

        var result = await MakeHandler(
            [t1, t2, t3],
            [AssignPrimary(t1.Id, myId), AssignPrimary(t2.Id, myId), AssignPrimary(t3.Id, myId)]
        ).Handle(new MyTicketDashboardStatsAsStaffQuery(), default);

        result.Data!.Sla.Met.Should().Be(2);
        result.Data.Sla.Breached.Should().Be(1);
        result.Data.Sla.CompliancePercent.Should().BeApproximately(66.67, 0.01);
    }
}
