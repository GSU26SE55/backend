using SharedInfrastructure.Services;
using SharedKernels.Interfaces;
using TicketService.Application.CQRS.Handler.Ticket;
using TicketService.Application.CQRS.Query.Ticket;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Queries;

public class MyTicketsAsCustomerQueryHandlerTests
{
    private readonly Mock<ITicketUnitOfWork> _mockUow = new();
    private readonly Mock<IGenericRepository<Ticket>> _mockRepo = new();
    private readonly Mock<ICurrentUserService> _mockCurrentUserService = new();
    private readonly MyTicketsAsCustomerQueryHandler _handler;

    public MyTicketsAsCustomerQueryHandlerTests()
    {
        _mockUow.Setup(x => x.Tickets).Returns(_mockRepo.Object);
        _handler = new MyTicketsAsCustomerQueryHandler(_mockUow.Object, _mockCurrentUserService.Object);
    }

    private static Ticket MakeTicket(Guid customerId, TicketStatusEnum status = TicketStatusEnum.Open) => new()
    {
        Id = Guid.NewGuid(),
        Code = "T-001",
        BatteryAssetId = Guid.NewGuid(),
        CustomerId = customerId,
        Title = "Test",
        Description = "desc",
        Category = TicketCategoryEnum.Other,
        Status = status,
        Origin = TicketOriginEnum.ManualByCustomer,
        CreatedAt = DateTime.UtcNow
    };

    private void SetupMock(List<Ticket> tickets)
        => _mockRepo.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<Ticket>(tickets));

    [Fact]
    public async Task Handle_ReturnsOnlyCurrentCustomerTickets()
    {
        var myId = Guid.NewGuid();
        _mockCurrentUserService.Setup(s => s.UserId).Returns(myId.ToString());
        SetupMock([MakeTicket(myId), MakeTicket(myId), MakeTicket(Guid.NewGuid())]);

        var result = await _handler.Handle(new MyTicketsAsCustomerQuery
        {
            PageNumber = 1,
            PageSize = 10
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Items.Should().HaveCount(2);
        result.Data.Items.Should().AllSatisfy(t => t.CustomerId.Should().Be(myId.ToString()));
    }

    [Fact]
    public async Task Handle_FilterByStatus_ReturnsMatchingOnly()
    {
        var myId = Guid.NewGuid();
        _mockCurrentUserService.Setup(s => s.UserId).Returns(myId.ToString());
        SetupMock([
            MakeTicket(myId, TicketStatusEnum.Open),
            MakeTicket(myId, TicketStatusEnum.Resolved),
            MakeTicket(myId, TicketStatusEnum.Closed)
        ]);

        var result = await _handler.Handle(new MyTicketsAsCustomerQuery
        {
            Status = TicketStatusEnum.Resolved,
            PageNumber = 1,
            PageSize = 10
        }, default);

        result.Data!.Items.Should().HaveCount(1);
        result.Data.Items[0].Status.Should().Be(TicketStatusEnum.Resolved);
    }

    [Fact]
    public async Task Handle_Pagination_ReturnsCorrectPage()
    {
        var myId = Guid.NewGuid();
        _mockCurrentUserService.Setup(s => s.UserId).Returns(myId.ToString());
        SetupMock(Enumerable.Range(1, 4).Select(_ => MakeTicket(myId)).ToList());

        var result = await _handler.Handle(new MyTicketsAsCustomerQuery
        {
            PageNumber = 2,
            PageSize = 3
        }, default);

        result.Data!.Items.Should().HaveCount(1);
        result.Data.TotalItems.Should().Be(4);
    }
}
