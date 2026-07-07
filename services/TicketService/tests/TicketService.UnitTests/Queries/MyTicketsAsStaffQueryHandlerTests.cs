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
    private readonly Mock<ITicketCurrentUserService> _mockCurrentUserService = new();
    private readonly MyTicketsAsStaffQueryHandler _handler;

    public MyTicketsAsStaffQueryHandlerTests()
    {
        _mockUow.Setup(x => x.Tickets).Returns(_mockRepo.Object);
        _handler = new MyTicketsAsStaffQueryHandler(_mockUow.Object, _mockCurrentUserService.Object);
    }

    private static Ticket MakeTicket(
        Guid? staffId,
        TicketStatusEnum status = TicketStatusEnum.InProgress,
        TicketPriorityEnum priority = TicketPriorityEnum.P3Normal,
        string code = "T-001") => new()
        {
            Id = Guid.NewGuid(),
            Code = code,
            BatteryAssetId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            AssignedStaffId = staffId,
            Title = "Test",
            Description = "desc",
            Category = TicketCategoryEnum.Other,
            Priority = priority,
            Status = status,
            Origin = TicketOriginEnum.ManualByCustomer,
            CreatedAt = DateTime.UtcNow
        };

    private void SetupMock(List<Ticket> tickets)
        => _mockRepo.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<Ticket>(tickets));

    [Fact]
    public async Task Handle_ReturnsOnlyAssignedTickets()
    {
        var myId = Guid.NewGuid();
        _mockCurrentUserService.Setup(s => s.UserId).Returns(myId.ToString());
        SetupMock([MakeTicket(myId), MakeTicket(myId), MakeTicket(Guid.NewGuid())]);

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
        SetupMock([
            MakeTicket(myId, TicketStatusEnum.InProgress),
            MakeTicket(myId, TicketStatusEnum.Resolved)
        ]);

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
        SetupMock([
            MakeTicket(myId, priority: TicketPriorityEnum.P3Normal, code: "P3"),
            MakeTicket(myId, priority: TicketPriorityEnum.P1Critical, code: "P1")
        ]);

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
        SetupMock([
            WithTimer(MakeTicket(myId, TicketStatusEnum.InProgress, code: "KEEP"), DateTime.UtcNow.AddHours(2)),
            MakeTicket(myId, TicketStatusEnum.InProgress, code: "NO-TIMER"),
            WithTimer(MakeTicket(myId, TicketStatusEnum.New, code: "NOT-MONITORED"), DateTime.UtcNow.AddHours(2)),
            WithTimer(MakeTicket(myId, TicketStatusEnum.Resolved, code: "RESOLVED"), DateTime.UtcNow.AddHours(2))
        ]);

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
        SetupMock([
            WithTimer(MakeTicket(myId, code: "FAR"), DateTime.UtcNow.AddHours(10)),
            WithTimer(MakeTicket(myId, code: "NEAR"), DateTime.UtcNow.AddHours(1)),
            MakeTicket(myId, code: "NO-TIMER")
        ]);

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
