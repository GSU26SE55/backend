using SharedKernels.Interfaces;
using TicketService.Application.CQRS.Handler.Ticket;
using TicketService.Application.CQRS.Query.Ticket;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Queries;

public class TicketGetListQueryHandlerTests
{
    private readonly Mock<ITicketUnitOfWork> _mockUow = new();
    private readonly Mock<IGenericRepository<Ticket>> _mockRepo = new();
    private readonly TicketGetListQueryHandler _handler;

    public TicketGetListQueryHandlerTests()
    {
        _mockUow.Setup(x => x.Tickets).Returns(_mockRepo.Object);
        _handler = new TicketGetListQueryHandler(_mockUow.Object);
    }

    private static Ticket MakeTicket(
        TicketStatusEnum status = TicketStatusEnum.Open,
        TicketPriorityEnum? priority = null,
        TicketCategoryEnum category = TicketCategoryEnum.Other,
        string title = "Test Ticket",
        string code = "T-001",
        Guid? batteryAssetId = null,
        bool isDeleted = false,
        DateTime? createdAt = null) => new()
        {
            Id = Guid.NewGuid(),
            Code = code,
            BatteryAssetId = batteryAssetId ?? Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Title = title,
            Description = "description",
            Category = category,
            Priority = priority,
            Status = status,
            Origin = TicketOriginEnum.ManualByCustomer,
            CreatedAt = createdAt ?? DateTime.UtcNow,
            IsDeleted = isDeleted
        };

    private void SetupMock(List<Ticket> tickets)
        => _mockRepo.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<Ticket>(tickets));

    [Fact]
    public async Task Handle_ReturnsOnlyNonDeletedTickets()
    {
        SetupMock([MakeTicket(), MakeTicket(isDeleted: true)]);

        var result = await _handler.Handle(new TicketGetListQuery { PageNumber = 1, PageSize = 10 }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Items.Should().HaveCount(1);
        result.Data.TotalItems.Should().Be(1);
    }

    [Fact]
    public async Task Handle_FilterByStatus_ReturnsMatchingOnly()
    {
        SetupMock([
            MakeTicket(TicketStatusEnum.Open),
            MakeTicket(TicketStatusEnum.Assigned),
            MakeTicket(TicketStatusEnum.InProgress)
        ]);

        var result = await _handler.Handle(new TicketGetListQuery
        {
            Status = TicketStatusEnum.Open,
            PageNumber = 1,
            PageSize = 10
        }, default);

        result.Data!.Items.Should().HaveCount(1);
        result.Data.Items[0].Status.Should().Be(TicketStatusEnum.Open);
    }

    [Fact]
    public async Task Handle_FilterByKeyword_MatchesTitleCaseInsensitive()
    {
        SetupMock([
            MakeTicket(title: "Battery Overheating Issue"),
            MakeTicket(title: "Charging Problem")
        ]);

        var result = await _handler.Handle(new TicketGetListQuery
        {
            Keyword = "overheat",
            PageNumber = 1,
            PageSize = 10
        }, default);

        result.Data!.Items.Should().HaveCount(1);
        result.Data.Items[0].Title.Should().Contain("Overheating");
    }

    [Fact]
    public async Task Handle_FilterByPriority_ReturnsMatchingOnly()
    {
        SetupMock([
            MakeTicket(priority: TicketPriorityEnum.P1Critical),
            MakeTicket(priority: TicketPriorityEnum.P2High),
            MakeTicket(priority: TicketPriorityEnum.P3Normal)
        ]);

        var result = await _handler.Handle(new TicketGetListQuery
        {
            Priority = TicketPriorityEnum.P1Critical,
            PageNumber = 1,
            PageSize = 10
        }, default);

        result.Data!.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_FilterByBatteryAssetId_ReturnsMatchingOnly()
    {
        var targetId = Guid.NewGuid();
        SetupMock([MakeTicket(batteryAssetId: targetId), MakeTicket()]);

        var result = await _handler.Handle(new TicketGetListQuery
        {
            BatteryAssetId = targetId,
            PageNumber = 1,
            PageSize = 10
        }, default);

        result.Data!.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_IsDescendingTrue_OrdersNewestFirst()
    {
        var now = DateTime.UtcNow;
        SetupMock([
            MakeTicket(code: "OLD", createdAt: now.AddHours(-2)),
            MakeTicket(code: "NEW", createdAt: now)
        ]);

        var result = await _handler.Handle(new TicketGetListQuery
        {
            IsDescending = true,
            PageNumber = 1,
            PageSize = 10
        }, default);

        result.Data!.Items[0].Code.Should().Be("NEW");
        result.Data.Items[1].Code.Should().Be("OLD");
    }

    [Fact]
    public async Task Handle_Pagination_ReturnsCorrectPage()
    {
        SetupMock(Enumerable.Range(1, 5).Select(_ => MakeTicket()).ToList());

        var result = await _handler.Handle(new TicketGetListQuery
        {
            PageNumber = 2,
            PageSize = 3
        }, default);

        result.Data!.Items.Should().HaveCount(2);
        result.Data.TotalItems.Should().Be(5);
        result.Data.PageNumber.Should().Be(2);
        result.Data.PageSize.Should().Be(3);
    }
}
