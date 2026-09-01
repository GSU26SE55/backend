using FluentAssertions;
using MockQueryable.Moq;
using Moq;
using SharedKernels.Interfaces;
using TicketService.Application.CQRS.Handler.Reports;
using TicketService.Application.CQRS.Query.Reports;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.Implements.Utils;

namespace TicketService.UnitTests.Reports;

/// <summary>Sprint 7 #114 (§5.2) — TicketService report handlers.</summary>
public class TicketReportHandlersTests
{
    private static Mock<IGenericRepository<T>> Repo<T>(List<T> data) where T : class
    {
        var repo = new Mock<IGenericRepository<T>>();
        repo.Setup(r => r.GetAllAsync()).Returns(data.AsQueryable().BuildMock());
        return repo;
    }

    private static Ticket Ticket(TicketCategoryEnum cat = TicketCategoryEnum.Charging, short? rating = null,
        int reopen = 0, TicketStatusEnum status = TicketStatusEnum.Open,
        DateTime? created = null, DateTime? resolved = null) => new()
        {
            Id = Guid.NewGuid(),
            Code = "T-1",
            Title = "t",
            Description = "d",
            Category = cat,
            Status = status,
            Rating = rating,
            RatedAt = rating != null ? DateTime.UtcNow : null,
            ReopenCount = reopen,
            CreatedAt = created ?? DateTime.UtcNow,
            ResolvedAt = resolved
        };

    [Fact]
    public async Task SlaByPriority_Counts_Met_Breached_And_Compliance()
    {
        var timers = new List<SlaTimer>
        {
            new() { Id = Guid.NewGuid(), TicketId = Guid.NewGuid(), Priority = TicketPriorityEnum.P1Critical, Status = SlaTimerStatusEnum.Met },
            new() { Id = Guid.NewGuid(), TicketId = Guid.NewGuid(), Priority = TicketPriorityEnum.P1Critical, Status = SlaTimerStatusEnum.Breached },
            new() { Id = Guid.NewGuid(), TicketId = Guid.NewGuid(), Priority = TicketPriorityEnum.P1Critical, Status = SlaTimerStatusEnum.Met }
        };
        var uow = new Mock<ITicketUnitOfWork>();
        uow.SetupGet(u => u.SlaTimers).Returns(Repo(timers).Object);

        var res = await new SlaByPriorityReportQueryHandler(uow.Object).Handle(new SlaByPriorityReportQuery(), default);

        res.Data.Should().HaveCount(3);   // P1/P2/P3 đều có row
        var p1 = res.Data!.Single(r => r.Priority == "P1Critical");
        p1.Met.Should().Be(2);
        p1.Breached.Should().Be(1);
        p1.ComplianceRate.Should().BeApproximately(66.67m, 0.01m);
    }

    [Fact]
    public async Task Csat_Computes_Avg_And_Distribution()
    {
        var tickets = new List<Ticket>
        {
            Ticket(rating: 5), Ticket(rating: 5), Ticket(rating: 3), Ticket()   // 1 chưa rate
        };
        var uow = new Mock<ITicketUnitOfWork>();
        uow.SetupGet(u => u.Tickets).Returns(Repo(tickets).Object);

        var res = await new CsatReportQueryHandler(uow.Object).Handle(new CsatReportQuery(), default);

        res.Data!.TotalRated.Should().Be(3);
        res.Data!.AvgRating.Should().BeApproximately(4.33m, 0.01m);
        res.Data!.RatingDistribution[5].Should().Be(2);
        res.Data!.RatingDistribution[3].Should().Be(1);
        res.Data!.RatingDistribution[1].Should().Be(0);
    }

    [Fact]
    public async Task ResolutionHistogram_Buckets_By_Duration()
    {
        var now = DateTime.UtcNow;
        var tickets = new List<Ticket>
        {
            Ticket(created: now.AddMinutes(-30), resolved: now),   // 0.5h → 0-1h
            Ticket(created: now.AddHours(-2), resolved: now),      // 2h → 1-4h
            Ticket(created: now.AddHours(-100), resolved: now)     // >3d
        };
        var uow = new Mock<ITicketUnitOfWork>();
        uow.SetupGet(u => u.Tickets).Returns(Repo(tickets).Object);

        var res = await new ResolutionTimeHistogramReportQueryHandler(uow.Object)
            .Handle(new ResolutionTimeHistogramReportQuery(), default);

        res.Data!.Single(b => b.Bucket == "0-1h").Count.Should().Be(1);
        res.Data!.Single(b => b.Bucket == "1-4h").Count.Should().Be(1);
        res.Data!.Single(b => b.Bucket == ">3d").Count.Should().Be(1);
    }

    [Fact]
    public async Task CategoryBreakdown_Groups_And_Orders()
    {
        var tickets = new List<Ticket>
        {
            Ticket(TicketCategoryEnum.Charging), Ticket(TicketCategoryEnum.Charging), Ticket(TicketCategoryEnum.Overheat)
        };
        var uow = new Mock<ITicketUnitOfWork>();
        uow.SetupGet(u => u.Tickets).Returns(Repo(tickets).Object);

        var res = await new CategoryBreakdownReportQueryHandler(uow.Object).Handle(new CategoryBreakdownReportQuery(), default);

        res.Data!.First().Category.Should().Be("Charging");
        res.Data!.First().Count.Should().Be(2);
    }

    [Fact]
    public async Task TopReopen_Only_Reopened_With_Avg()
    {
        var tickets = new List<Ticket>
        {
            Ticket(TicketCategoryEnum.Charging, reopen: 2),
            Ticket(TicketCategoryEnum.Charging, reopen: 4),
            Ticket(TicketCategoryEnum.Overheat, reopen: 0)   // không reopen → loại
        };
        var uow = new Mock<ITicketUnitOfWork>();
        uow.SetupGet(u => u.Tickets).Returns(Repo(tickets).Object);

        var res = await new TopReopenIssuesReportQueryHandler(uow.Object).Handle(new TopReopenIssuesReportQuery(), default);

        res.Data.Should().HaveCount(1);
        res.Data!.Single().Category.Should().Be("Charging");
        res.Data!.Single().Count.Should().Be(2);
        res.Data!.Single().AvgReopenCount.Should().Be(3.0m);
    }

    // ── QA Bug #20 — Dual-Timer Architecture breach liability and Rescue KPI ─────

    [Fact]
    public async Task SlaByStaff_AttributesBreachLiability_ToPreviousPrimaryHandler()
    {
        // Ticket T1 đã vi phạm SLA: Staff A là PreviousPrimaryHandler (người gây vi phạm),
        // Staff B là PrimaryHandler mới (người tiếp cứu). Chỉ Staff A bị tính Breached.
        var ticketId = Guid.NewGuid();
        var staffAId = Guid.NewGuid();
        var staffBId = Guid.NewGuid();

        var timer = new SlaTimer { Id = Guid.NewGuid(), TicketId = ticketId, Priority = TicketPriorityEnum.P2High, Status = SlaTimerStatusEnum.Breached, Type = SlaTimerTypeEnum.Resolution };
        var ticket = new Ticket { Id = ticketId, Code = "T-BREACH", Title = "t", Description = "d", SlaTimers = new List<SlaTimer> { timer } };

        var assignments = new List<TicketAssignment>
        {
            new() { Id = Guid.NewGuid(), TicketId = ticketId, StaffId = staffAId, Role = AssignmentRoleEnum.PreviousPrimaryHandler },
            new() { Id = Guid.NewGuid(), TicketId = ticketId, StaffId = staffBId, Role = AssignmentRoleEnum.PrimaryHandler }
        };
        var staff = new List<StaffAccount>
        {
            new() { Id = staffAId, AccountId = staffAId, FullName = "Staff A" },
            new() { Id = staffBId, AccountId = staffBId, FullName = "Staff B" }
        };

        var uow = new Mock<ITicketUnitOfWork>();
        uow.SetupGet(u => u.TicketAssignments).Returns(Repo(assignments).Object);
        uow.SetupGet(u => u.Tickets).Returns(Repo(new List<Ticket> { ticket }).Object);
        uow.SetupGet(u => u.StaffAccounts).Returns(Repo(staff).Object);

        var res = await new SlaByStaffReportQueryHandler(uow.Object).Handle(new SlaByStaffReportQuery(), default);

        var rowA = res.Data!.Single(r => r.StaffId == staffAId.ToString());
        var rowB = res.Data!.Single(r => r.StaffId == staffBId.ToString());
        rowA.Breached.Should().Be(1, "PreviousPrimaryHandler owns the breach liability");
        rowA.Met.Should().Be(0);
        rowB.Breached.Should().Be(0, "rescue PrimaryHandler must not be penalised for the breach");
        rowB.TotalAssigned.Should().Be(1);
    }

    [Fact]
    public async Task StaffPerformance_Rescue_KPI_CountsAttemptAndSuccess()
    {
        // Staff B nhận ticket đã Breached, giải quyết trong 60 phút làm việc (<= 1440) → success.
        var ticketId = Guid.NewGuid();
        var staffBId = Guid.NewGuid();
        var assignmentCreatedAt = new DateTime(2026, 8, 11, 7, 0, 0, DateTimeKind.Utc);  // 14:00 HCM — trong giờ làm
        var resolvedAt = assignmentCreatedAt.AddMinutes(60);

        var timer = new SlaTimer { Id = Guid.NewGuid(), TicketId = ticketId, Priority = TicketPriorityEnum.P2High, Status = SlaTimerStatusEnum.Breached, BreachAt = assignmentCreatedAt.AddHours(-1), Type = SlaTimerTypeEnum.Resolution };
        var ticket = new Ticket
        {
            Id = ticketId,
            Code = "T-RESCUE",
            Title = "t",
            Description = "d",
            Status = TicketStatusEnum.Completed,
            CreatedAt = assignmentCreatedAt.AddHours(-5),
            ResolvedAt = resolvedAt,
            SlaTimers = new List<SlaTimer> { timer }
        };
        var assignments = new List<TicketAssignment>
        {
            new() { Id = Guid.NewGuid(), TicketId = ticketId, StaffId = staffBId, Role = AssignmentRoleEnum.PrimaryHandler, CreatedAt = assignmentCreatedAt }
        };
        var staff = new List<StaffAccount>
        {
            new() { Id = staffBId, AccountId = staffBId, FullName = "Staff B" }
        };

        var uow = new Mock<ITicketUnitOfWork>();
        uow.SetupGet(u => u.TicketAssignments).Returns(Repo(assignments).Object);
        uow.SetupGet(u => u.Tickets).Returns(Repo(new List<Ticket> { ticket }).Object);
        uow.SetupGet(u => u.StaffAccounts).Returns(Repo(staff).Object);

        var res = await new StaffPerformanceReportQueryHandler(uow.Object, new SlaCalculator())
            .Handle(new StaffPerformanceReportQuery(), default);

        var row = res.Data!.Single(r => r.StaffId == staffBId.ToString());
        row.RescueCount.Should().Be(1, "ticket was Breached when staff B took over");
        row.RescueSuccessCount.Should().Be(1, "resolved within 1440 working minutes");
    }
}
