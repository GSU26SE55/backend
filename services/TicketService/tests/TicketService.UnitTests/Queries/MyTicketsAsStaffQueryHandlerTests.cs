using MockQueryable.Moq;
using SharedKernels.Interfaces;
using TicketService.Application.CQRS.Handler.Ticket;
using TicketService.Application.CQRS.Query.Ticket;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Queries;

public class MyTicketsAsStaffQueryHandlerTests
{
    private readonly Mock<ITicketUnitOfWork> _mockUow = new();
    private readonly Mock<IGenericRepository<Ticket>> _mockRepo = new();
    private readonly Mock<IGenericRepository<TicketAssignment>> _mockAssignments = new();
    private readonly Mock<ITicketCurrentUserService> _mockCurrentUserService = new();
    private readonly MyTicketsAsStaffQueryHandler _handler;

    public MyTicketsAsStaffQueryHandlerTests()
    {
        _mockUow.Setup(x => x.Tickets).Returns(_mockRepo.Object);
        _mockUow.Setup(x => x.TicketAssignments).Returns(_mockAssignments.Object);

        var mockChatReads = new Mock<IGenericRepository<TicketChatRead>>();
        mockChatReads.Setup(r => r.GetAllAsync()).Returns(Array.Empty<TicketChatRead>().BuildMock());
        _mockUow.Setup(x => x.TicketChatReads).Returns(mockChatReads.Object);

        var mockChats = new Mock<IGenericRepository<TicketChat>>();
        mockChats.Setup(r => r.GetAllAsync()).Returns(Array.Empty<TicketChat>().BuildMock());
        _mockUow.Setup(x => x.TicketChats).Returns(mockChats.Object);

        _handler = new MyTicketsAsStaffQueryHandler(_mockUow.Object, _mockCurrentUserService.Object);
    }

    private static Ticket MakeTicket(
        TicketStatusEnum status = TicketStatusEnum.InProgress,
        TicketPriorityEnum priority = TicketPriorityEnum.P3Normal,
        string code = "T-001") => new()
        {
            Id = Guid.NewGuid(),
            Code = code,
            BatteryAssetId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Title = "Test",
            Description = "desc",
            Category = TicketCategoryEnum.Other,
            Priority = priority,
            Status = status,
            Origin = TicketOriginEnum.ManualByCustomer,
            CreatedAt = DateTime.UtcNow
        };

    private static TicketAssignment AssignPrimary(Guid ticketId, Guid staffId) => new()
    {
        Id = Guid.NewGuid(),
        TicketId = ticketId,
        StaffId = staffId,
        Role = AssignmentRoleEnum.PrimaryHandler
    };

    private void SetupMock(List<Ticket> tickets, List<TicketAssignment>? assignments = null)
    {
        _mockRepo.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<Ticket>(tickets));
        _mockAssignments.Setup(r => r.GetAllAsync())
            .Returns((assignments ?? new List<TicketAssignment>()).BuildMock());
    }

    [Fact]
    public async Task Handle_ReturnsOnlyAssignedTickets()
    {
        var myId = Guid.NewGuid();
        _mockCurrentUserService.Setup(s => s.UserId).Returns(myId.ToString());

        var mine1 = MakeTicket();
        var mine2 = MakeTicket();
        var other = MakeTicket();

        SetupMock(
            [mine1, mine2, other],
            [AssignPrimary(mine1.Id, myId), AssignPrimary(mine2.Id, myId)]);

        var result = await _handler.Handle(new MyTicketsAsStaffQuery
        {
            PageNumber = 1,
            PageSize = 10
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_FilterByStatus_ReturnsMatchingOnly()
    {
        var myId = Guid.NewGuid();
        _mockCurrentUserService.Setup(s => s.UserId).Returns(myId.ToString());

        var t1 = MakeTicket(TicketStatusEnum.InProgress);
        var t2 = MakeTicket(TicketStatusEnum.Resolved);

        SetupMock(
            [t1, t2],
            [AssignPrimary(t1.Id, myId), AssignPrimary(t2.Id, myId)]);

        var result = await _handler.Handle(new MyTicketsAsStaffQuery
        {
            Status = TicketStatusEnum.InProgress,
            PageNumber = 1,
            PageSize = 10
        }, default);

        result.Data!.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_OrdersByPriorityAscending()
    {
        var myId = Guid.NewGuid();
        _mockCurrentUserService.Setup(s => s.UserId).Returns(myId.ToString());

        var tP3 = MakeTicket(priority: TicketPriorityEnum.P3Normal, code: "P3");
        var tP1 = MakeTicket(priority: TicketPriorityEnum.P1Critical, code: "P1");

        SetupMock(
            [tP3, tP1],
            [AssignPrimary(tP3.Id, myId), AssignPrimary(tP1.Id, myId)]);

        var result = await _handler.Handle(new MyTicketsAsStaffQuery
        {
            PageNumber = 1,
            PageSize = 10
        }, default);

        result.Data!.Items[0].Code.Should().Be("P1");
        result.Data.Items[1].Code.Should().Be("P3");
    }

    private static Ticket WithTimer(Ticket ticket, DateTime dueAt)
    {
        ticket.SlaTimer = new SlaTimer
        {
            Id = Guid.NewGuid(),
            Status = SlaTimerStatusEnum.Running,
            StartedAt = DateTime.UtcNow.AddHours(-1),
            DueAt = dueAt
        };
        return ticket;
    }

    [Fact]
    public async Task Handle_SlaOpenFilter_ReturnsOnlyMonitoredWithTimer()
    {
        var myId = Guid.NewGuid();
        _mockCurrentUserService.Setup(s => s.UserId).Returns(myId.ToString());

        var keep = WithTimer(MakeTicket(TicketStatusEnum.InProgress, code: "KEEP"), DateTime.UtcNow.AddHours(2));
        var noTimer = MakeTicket(TicketStatusEnum.InProgress, code: "NO-TIMER");
        var notMonitor = WithTimer(MakeTicket(TicketStatusEnum.New, code: "NOT-MONITORED"), DateTime.UtcNow.AddHours(2));
        var resolved = WithTimer(MakeTicket(TicketStatusEnum.Resolved, code: "RESOLVED"), DateTime.UtcNow.AddHours(2));

        SetupMock(
            [keep, noTimer, notMonitor, resolved],
            [AssignPrimary(keep.Id, myId), AssignPrimary(noTimer.Id, myId),
             AssignPrimary(notMonitor.Id, myId), AssignPrimary(resolved.Id, myId)]);

        var result = await _handler.Handle(new MyTicketsAsStaffQuery
        {
            SlaOpen = true,
            PageNumber = 1,
            PageSize = 10
        }, default);

        result.Data!.TotalItems.Should().Be(1);
        result.Data.Items[0].Code.Should().Be("KEEP");
    }

    [Fact]
    public async Task Handle_SortBySlaRemaining_NearestDueFirst_NoTimerLast()
    {
        var myId = Guid.NewGuid();
        _mockCurrentUserService.Setup(s => s.UserId).Returns(myId.ToString());

        var far = WithTimer(MakeTicket(code: "FAR"), DateTime.UtcNow.AddHours(10));
        var near = WithTimer(MakeTicket(code: "NEAR"), DateTime.UtcNow.AddHours(1));
        var noTimer = MakeTicket(code: "NO-TIMER");

        SetupMock(
            [far, near, noTimer],
            [AssignPrimary(far.Id, myId), AssignPrimary(near.Id, myId), AssignPrimary(noTimer.Id, myId)]);

        var result = await _handler.Handle(new MyTicketsAsStaffQuery
        {
            SortBy = "slaRemaining",
            PageNumber = 1,
            PageSize = 10
        }, default);

        result.Data!.Items[0].Code.Should().Be("NEAR");
        result.Data.Items[1].Code.Should().Be("FAR");
        result.Data.Items[2].Code.Should().Be("NO-TIMER");
    }
}
