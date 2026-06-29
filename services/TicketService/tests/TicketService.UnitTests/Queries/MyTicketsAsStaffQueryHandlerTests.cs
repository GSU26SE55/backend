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
}
