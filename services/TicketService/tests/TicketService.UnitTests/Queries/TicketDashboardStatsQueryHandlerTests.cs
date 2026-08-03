using TicketService.Application.CQRS.Handler.Ticket;
using TicketService.Application.CQRS.Query.Ticket;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Queries;

public class TicketDashboardStatsQueryHandlerTests
{
    private static Ticket MakeTicket(
        TicketStatusEnum status = TicketStatusEnum.InProgress,
        TicketPriorityEnum? priority = TicketPriorityEnum.P3Normal,
        Guid? staffId = null,
        SlaTimer? slaTimer = null,
        DateTime? createdAt = null,
        bool isDeleted = false) => new()
        {
            Id = Guid.NewGuid(),
            Code = "T-001",
            BatteryAssetId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            PrimaryHandlerStaffId = staffId,
            Title = "Test",
            Description = "desc",
            Category = TicketCategoryEnum.Other,
            Priority = priority,
            Status = status,
            Origin = TicketOriginEnum.ManualByCustomer,
            SlaTimer = slaTimer,
            CreatedAt = createdAt ?? DateTime.UtcNow,
            IsDeleted = isDeleted
        };

    private static SlaTimer MakeTimer(SlaTimerStatusEnum status) => new()
    {
        Id = Guid.NewGuid(),
        Status = status,
        StartedAt = DateTime.UtcNow.AddHours(-1),
        DueAt = DateTime.UtcNow.AddHours(3),
        OriginalDueAt = DateTime.UtcNow.AddHours(3)
    };

    private static TicketDashboardStatsQueryHandler MakeHandler(params Ticket[] tickets)
    {
        var assignments = tickets
            .Where(t => t.PrimaryHandlerStaffId.HasValue)
            .Select(t => new TicketAssignment
            {
                Id = Guid.NewGuid(),
                TicketId = t.Id,
                StaffId = t.PrimaryHandlerStaffId!.Value,
                Role = AssignmentRoleEnum.PrimaryHandler
            });
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(
            ticketSeed: tickets,
            assignmentSeed: assignments);
        return new TicketDashboardStatsQueryHandler(uow.Object);
    }

    [Fact]
    public async Task Handle_Empty_ReturnsZeroFilledStats()
    {
        var result = await MakeHandler().Handle(new TicketDashboardStatsQuery(), default);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Total.Should().Be(0);
        result.Data.OpenCount.Should().Be(0);
        result.Data.CountByStatus.Should().HaveCount(Enum.GetValues<TicketStatusEnum>().Length);
        result.Data.CountByStatus.Values.Should().AllBeEquivalentTo(0);
        result.Data.CountByPriority.Should().HaveCount(3);
        result.Data.Sla.CompliancePercent.Should().Be(100);
        result.Data.CreatedTrend7Days.Should().HaveCount(7);
        result.Data.CreatedTrend7Days.Sum(p => p.Count).Should().Be(0);
        result.Data.OpenCountByStaff.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_CountsByStatus_OpenExcludesTerminal()
    {
        var result = await MakeHandler(
            MakeTicket(TicketStatusEnum.New),
            MakeTicket(TicketStatusEnum.InProgress),
            MakeTicket(TicketStatusEnum.Resolved),
            MakeTicket(TicketStatusEnum.ClosedRejected),
            MakeTicket(TicketStatusEnum.Closed),
            MakeTicket(TicketStatusEnum.InProgress, isDeleted: true) // bị xóa — không tính
        ).Handle(new TicketDashboardStatsQuery(), default);

        result.Data!.Total.Should().Be(5);
        result.Data.OpenCount.Should().Be(2); // New + InProgress
        result.Data.CountByStatus["New"].Should().Be(1);
        result.Data.CountByStatus["InProgress"].Should().Be(1);
        result.Data.CountByStatus["Resolved"].Should().Be(1);
        result.Data.CountByStatus["ClosedRejected"].Should().Be(1);
        result.Data.CountByStatus["Closed"].Should().Be(1);
        result.Data.CountByStatus["Escalated"].Should().Be(0); // zero-fill
    }

    [Fact]
    public async Task Handle_SlaSummary_ComputesCompliancePercent()
    {
        var result = await MakeHandler(
            MakeTicket(slaTimer: MakeTimer(SlaTimerStatusEnum.Met)),
            MakeTicket(slaTimer: MakeTimer(SlaTimerStatusEnum.Met)),
            MakeTicket(slaTimer: MakeTimer(SlaTimerStatusEnum.Met)),
            MakeTicket(slaTimer: MakeTimer(SlaTimerStatusEnum.Breached)),
            MakeTicket(slaTimer: MakeTimer(SlaTimerStatusEnum.Running)),
            MakeTicket(slaTimer: MakeTimer(SlaTimerStatusEnum.Paused)),
            MakeTicket() // không có timer — không tính vào SLA
        ).Handle(new TicketDashboardStatsQuery(), default);

        result.Data!.Sla.Met.Should().Be(3);
        result.Data.Sla.Breached.Should().Be(1);
        result.Data.Sla.Running.Should().Be(1);
        result.Data.Sla.Paused.Should().Be(1);
        result.Data.Sla.CompliancePercent.Should().Be(75); // 3/(3+1)
    }

    [Fact]
    public async Task Handle_CountByPriority_SkipsUntriaged()
    {
        var result = await MakeHandler(
            MakeTicket(priority: TicketPriorityEnum.P1Critical),
            MakeTicket(priority: TicketPriorityEnum.P3Normal),
            MakeTicket(priority: TicketPriorityEnum.P3Normal),
            MakeTicket(priority: null) // chưa triage
        ).Handle(new TicketDashboardStatsQuery(), default);

        result.Data!.CountByPriority["P1Critical"].Should().Be(1);
        result.Data.CountByPriority["P2High"].Should().Be(0); // zero-fill
        result.Data.CountByPriority["P3Normal"].Should().Be(2);
    }

    [Fact]
    public async Task Handle_Trend7Days_BucketsByCreatedDate()
    {
        var today = DateTime.UtcNow;
        var result = await MakeHandler(
            MakeTicket(createdAt: today),
            MakeTicket(createdAt: today.AddDays(-3)),
            MakeTicket(createdAt: today.AddDays(-10)) // ngoài cửa sổ 7 ngày
        ).Handle(new TicketDashboardStatsQuery(), default);

        result.Data!.CreatedTrend7Days.Should().HaveCount(7);
        result.Data.CreatedTrend7Days.Sum(p => p.Count).Should().Be(2);
        result.Data.CreatedTrend7Days[^1].Date.Should().Be(DateOnly.FromDateTime(today.Date));
        result.Data.CreatedTrend7Days[^1].Count.Should().Be(1);
    }

    [Fact]
    public async Task Handle_OpenCountByStaff_CountsOnlyOpenAssigned()
    {
        var staff1 = Guid.NewGuid();
        var staff2 = Guid.NewGuid();
        var result = await MakeHandler(
            MakeTicket(TicketStatusEnum.InProgress, staffId: staff1),
            MakeTicket(TicketStatusEnum.Assigned, staffId: staff1),
            MakeTicket(TicketStatusEnum.Resolved, staffId: staff1), // đã xử lý — không tính workload
            MakeTicket(TicketStatusEnum.InProgress, staffId: staff2),
            MakeTicket(TicketStatusEnum.InProgress) // chưa gán — không tính
        ).Handle(new TicketDashboardStatsQuery(), default);

        result.Data!.OpenCountByStaff.Should().HaveCount(2);
        result.Data.OpenCountByStaff[0].StaffId.Should().Be(staff1.ToString()); // sort giảm dần
        result.Data.OpenCountByStaff[0].ActiveCount.Should().Be(2);
        result.Data.OpenCountByStaff[1].ActiveCount.Should().Be(1);
    }
}
