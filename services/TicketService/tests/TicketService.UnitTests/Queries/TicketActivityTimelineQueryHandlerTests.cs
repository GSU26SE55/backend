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

    // Không nhận PrimaryHandlerStaffId: handler resolve staff qua t.Assignments, set cột đó
    // không có tác dụng — muốn ticket có primary handler thì Add vào ticket.Assignments.
    private static Ticket MakeTicket(Guid? customerId = null) => new()
    {
        Id = Guid.NewGuid(),
        Code = "T-001",
        BatteryAssetId = Guid.NewGuid(),
        CustomerId = customerId ?? Guid.NewGuid(),
        Title = "Test",
        Description = "desc",
        Category = TicketCategoryEnum.Other,
        Status = TicketStatusEnum.InProgress,
        Origin = TicketOriginEnum.ManualByCustomer,
        CreatedAt = DateTime.UtcNow
    };

    private static TicketActivity MakeActivity(Ticket ticket, ActivityActionEnum action = ActivityActionEnum.StatusChanged) => new()
    {
        Id = Guid.NewGuid(),
        TicketId = ticket.Id,
        Ticket = ticket,
        ActorRole = ActorRoleEnum.Staff,
        Action = action,
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

    [Theory]
    [InlineData(ActivityActionEnum.Chatted)]
    [InlineData(ActivityActionEnum.ChatEdited)]
    [InlineData(ActivityActionEnum.ChatDeleted)]
    [InlineData(ActivityActionEnum.ChatRestored)]
    [InlineData(ActivityActionEnum.ChatReplied)]
    [InlineData(ActivityActionEnum.ChatPinned)]
    [InlineData(ActivityActionEnum.ChatUnpinned)]
    [InlineData(ActivityActionEnum.ChatFlagged)]
    [InlineData(ActivityActionEnum.ParticipantAdded)]
    [InlineData(ActivityActionEnum.ParticipantRemoved)]
    [InlineData(ActivityActionEnum.ParticipantRoleChanged)]
    public async Task Handle_CustomerDoesNotSeeInternalActions(ActivityActionEnum internalAction)
    {
        var customerId = Guid.NewGuid();
        var ticket = MakeTicket(customerId: customerId);
        _mockTicketRepo.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<Ticket>(new List<Ticket> { ticket }));
        _mockActivityRepo.Setup(r => r.GetAllAsync())
            .Returns(() => new TestAsyncEnumerable<TicketActivity>(new List<TicketActivity>
            {
                MakeActivity(ticket, internalAction),
                MakeActivity(ticket, ActivityActionEnum.StatusChanged)
            }));

        var result = await _handler.Handle(new TicketActivityTimelineQuery
        {
            TicketId = ticket.Id,
            ActorUserId = customerId,
            ActorRoles = ["Customer"]
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().ContainSingle()
            .Which.Action.Should().Be(ActivityActionEnum.StatusChanged);
    }

    [Fact]
    public async Task Handle_CustomerStillSeesLifecycleActions()
    {
        var customerId = Guid.NewGuid();
        var ticket = MakeTicket(customerId: customerId);
        _mockTicketRepo.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<Ticket>(new List<Ticket> { ticket }));
        _mockActivityRepo.Setup(r => r.GetAllAsync())
            .Returns(() => new TestAsyncEnumerable<TicketActivity>(new List<TicketActivity>
            {
                MakeActivity(ticket, ActivityActionEnum.Created),
                MakeActivity(ticket, ActivityActionEnum.StatusChanged),
                MakeActivity(ticket, ActivityActionEnum.Resolved),
                MakeActivity(ticket, ActivityActionEnum.Rated),
                MakeActivity(ticket, ActivityActionEnum.SlaBreached)
            }));

        var result = await _handler.Handle(new TicketActivityTimelineQuery
        {
            TicketId = ticket.Id,
            ActorUserId = customerId,
            ActorRoles = ["Customer"]
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().HaveCount(5);
    }

    [Fact]
    public async Task Handle_StaffSeesInternalActions()
    {
        var staffId = Guid.NewGuid();
        // Handler resolve primary handler qua t.Assignments, KHÔNG qua cột PrimaryHandlerStaffId.
        var ticket = MakeTicket();
        ticket.Assignments.Add(new TicketAssignment
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            StaffId = staffId,
            Role = AssignmentRoleEnum.PrimaryHandler
        });
        _mockTicketRepo.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<Ticket>(new List<Ticket> { ticket }));
        _mockActivityRepo.Setup(r => r.GetAllAsync())
            .Returns(() => new TestAsyncEnumerable<TicketActivity>(new List<TicketActivity>
            {
                MakeActivity(ticket, ActivityActionEnum.Chatted),
                MakeActivity(ticket, ActivityActionEnum.StatusChanged)
            }));

        var result = await _handler.Handle(new TicketActivityTimelineQuery
        {
            TicketId = ticket.Id,
            ActorUserId = staffId,
            ActorRoles = ["Staff"]
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_ParticipantWithCanViewInternal_SeesInternalActions()
    {
        var participantId = Guid.NewGuid();
        var ticket = MakeTicket();
        _mockTicketRepo.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<Ticket>(new List<Ticket> { ticket }));
        _mockParticipantRepo.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<TicketParticipant>(
        [
            new TicketParticipant
            {
                Id = Guid.NewGuid(),
                TicketId = ticket.Id,
                Ticket = ticket,
                UserId = participantId,
                UserRole = ActorRoleEnum.Customer,
                ParticipantType = ParticipantTypeEnum.Watcher,
                CanViewInternal = true,
                AddedByUserId = Guid.NewGuid(),
                AddedAt = DateTime.UtcNow
            }
        ]));
        _mockActivityRepo.Setup(r => r.GetAllAsync())
            .Returns(() => new TestAsyncEnumerable<TicketActivity>(new List<TicketActivity>
            {
                MakeActivity(ticket, ActivityActionEnum.Chatted),
                MakeActivity(ticket, ActivityActionEnum.StatusChanged)
            }));

        var result = await _handler.Handle(new TicketActivityTimelineQuery
        {
            TicketId = ticket.Id,
            ActorUserId = participantId,
            ActorRoles = ["Customer"]
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().HaveCount(2);
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
