using SharedKernels.Interfaces;
using TicketService.Application.CQRS.Query.ManagerQueue;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.UnitTests.Queries;

public class ManagerQueueQueryHandlerTests
{
    private readonly Mock<ITicketUnitOfWork> _mockUow = new();
    private readonly Mock<IGenericRepository<Ticket>> _mockRepo = new();
    private readonly ManagerQueueQueryHandler _handler;

    public ManagerQueueQueryHandlerTests()
    {
        _mockUow.Setup(x => x.Tickets).Returns(_mockRepo.Object);
        _handler = new ManagerQueueQueryHandler(_mockUow.Object);
    }

    private static Ticket MakeTicket(
        TicketStatusEnum status = TicketStatusEnum.Open,
        TicketPriorityEnum priority = TicketPriorityEnum.P3Normal,
        TicketCategoryEnum category = TicketCategoryEnum.Other,
        string code = "T-001",
        DateTime? createdAt = null) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        BatteryAssetId = Guid.NewGuid(),
        CustomerId = Guid.NewGuid(),
        Title = "Test",
        Description = "desc",
        Category = category,
        Priority = priority,
        Status = status,
        Origin = TicketOriginEnum.ManualByCustomer,
        CreatedAt = createdAt ?? DateTime.UtcNow
    };

    private void SetupMock(List<Ticket> tickets)
        => _mockRepo.Setup(r => r.GetAllAsync()).Returns(tickets.BuildMockDbSet().Object);

    [Fact]
    public async Task Handle_ReturnsOnlyOpenTickets()
    {
        SetupMock([
            MakeTicket(TicketStatusEnum.Open),
            MakeTicket(TicketStatusEnum.Assigned),
            MakeTicket(TicketStatusEnum.InProgress)
        ]);

        var result = await _handler.Handle(new ManagerQueueQuery { PageNumber = 1, PageSize = 10 }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Items.Should().HaveCount(1);
        result.Data.Items[0].Status.Should().Be(TicketStatusEnum.Open);
    }

    [Fact]
    public async Task Handle_OrdersByPriorityAscending_P1First()
    {
        var now = DateTime.UtcNow;
        SetupMock([
            MakeTicket(priority: TicketPriorityEnum.P3Normal, code: "P3", createdAt: now),
            MakeTicket(priority: TicketPriorityEnum.P1Critical, code: "P1", createdAt: now),
            MakeTicket(priority: TicketPriorityEnum.P2High, code: "P2", createdAt: now)
        ]);

        var result = await _handler.Handle(new ManagerQueueQuery { PageNumber = 1, PageSize = 10 }, default);

        result.Data!.Items[0].Code.Should().Be("P1");
        result.Data.Items[1].Code.Should().Be("P2");
        result.Data.Items[2].Code.Should().Be("P3");
    }

    [Fact]
    public async Task Handle_FilterByPriority_ReturnsMatchingOnly()
    {
        SetupMock([
            MakeTicket(priority: TicketPriorityEnum.P1Critical),
            MakeTicket(priority: TicketPriorityEnum.P2High)
        ]);

        var result = await _handler.Handle(new ManagerQueueQuery
        {
            Priority = TicketPriorityEnum.P1Critical, PageNumber = 1, PageSize = 10
        }, default);

        result.Data!.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_FilterByCategory_ReturnsMatchingOnly()
    {
        SetupMock([
            MakeTicket(category: TicketCategoryEnum.Overheat),
            MakeTicket(category: TicketCategoryEnum.Charging)
        ]);

        var result = await _handler.Handle(new ManagerQueueQuery
        {
            Category = TicketCategoryEnum.Overheat, PageNumber = 1, PageSize = 10
        }, default);

        result.Data!.Items.Should().HaveCount(1);
    }
}
