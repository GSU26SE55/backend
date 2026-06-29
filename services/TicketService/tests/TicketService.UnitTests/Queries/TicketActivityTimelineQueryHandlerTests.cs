using SharedKernels.Interfaces;
using TicketService.Application.CQRS.Query.Ticket;
using TicketService.Application.CQRS.Query.TicketActivityTimeline;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Queries;

public class TicketActivityTimelineQueryHandlerTests
{
    private readonly Mock<ITicketUnitOfWork> _mockUow = new();
    private readonly Mock<IGenericRepository<Ticket>> _mockTicketRepo = new();
    private readonly Mock<IGenericRepository<TicketActivity>> _mockActivityRepo = new();
    private readonly Mock<IGenericRepository<TicketParticipant>> _mockParticipantRepo = new();
    private readonly TicketActivityTimelineQueryHandler _handler;

    public TicketActivityTimelineQueryHandlerTests()
    {
        _mockUow.Setup(x => x.Tickets).Returns(_mockTicketRepo.Object);
        _mockUow.Setup(x => x.TicketActivities).Returns(_mockActivityRepo.Object);
        _mockParticipantRepo.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<TicketParticipant>([]));
        _mockUow.Setup(x => x.TicketParticipants).Returns(_mockParticipantRepo.Object);
        _handler = new TicketActivityTimelineQueryHandler(_mockUow.Object);
    }

    private static Ticket MakeTicket(Guid? customerId = null, Guid? assignedStaffId = null) => new()
    {
        Id = Guid.NewGuid(),
        Code = "T-001",
        BatteryAssetId = Guid.NewGuid(),
        CustomerId = customerId ?? Guid.NewGuid(),
        AssignedStaffId = assignedStaffId,
        Title = "Test",
        Description = "desc",
        Category = TicketCategoryEnum.Other,
        Status = TicketStatusEnum.InProgress,
        Origin = TicketOriginEnum.ManualByCustomer,
        CreatedAt = DateTime.UtcNow
    };

    private static TicketActivity MakeActivity(Ticket ticket) => new()
    {
        Id = Guid.NewGuid(),
        TicketId = ticket.Id,
        Ticket = ticket,
        ActorRole = ActorRoleEnum.Staff,
        Action = ActivityActionEnum.StatusChanged,
        CreatedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task Handle_AdminCanViewActivities_Returns200WithItems()
    {
        var ticket = MakeTicket();
        _mockTicketRepo.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<Ticket>(new List<Ticket> { ticket }));
        _mockActivityRepo.Setup(r => r.GetAllAsync())
            .Returns(() => new TestAsyncEnumerable<TicketActivity>(new List<TicketActivity> { MakeActivity(ticket), MakeActivity(ticket) }));

        var result = await _handler.Handle(new TicketActivityTimelineQuery
        {
            TicketId = ticket.Id,
            ActorUserId = Guid.NewGuid(),
            ActorRoles = ["Admin"]
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.Data.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_CustomerCanViewOwnTicketActivities_Returns200()
    {
        var customerId = Guid.NewGuid();
        var ticket = MakeTicket(customerId: customerId);
        _mockTicketRepo.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<Ticket>(new List<Ticket> { ticket }));
        _mockActivityRepo.Setup(r => r.GetAllAsync())
            .Returns(() => new TestAsyncEnumerable<TicketActivity>(new List<TicketActivity> { MakeActivity(ticket) }));

        var result = await _handler.Handle(new TicketActivityTimelineQuery
        {
            TicketId = ticket.Id,
            ActorUserId = customerId,
            ActorRoles = ["Customer"]
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Handle_CustomerCannotViewOtherTicketActivities_Returns403()
    {
        var ticket = MakeTicket(customerId: Guid.NewGuid());
        _mockTicketRepo.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<Ticket>(new List<Ticket> { ticket }));

        var result = await _handler.Handle(new TicketActivityTimelineQuery
        {
            TicketId = ticket.Id,
            ActorUserId = Guid.NewGuid(),
            ActorRoles = ["Customer"]
        }, default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_TicketNotFound_Returns404()
    {
        _mockTicketRepo.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<Ticket>(new List<Ticket>()));

        var result = await _handler.Handle(new TicketActivityTimelineQuery
        {
            TicketId = Guid.NewGuid(),
            ActorUserId = Guid.NewGuid(),
            ActorRoles = ["Admin"]
        }, default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }
}
