using SharedKernels.Interfaces;
using TicketService.Application.CQRS.Handler.Ticket;
using TicketService.Application.CQRS.Query.Ticket;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Queries;

public class TicketGetByIdQueryHandlerTests
{
    private readonly Mock<ITicketUnitOfWork> _mockUow = new();
    private readonly Mock<IGenericRepository<Ticket>> _mockRepo = new();
    private readonly Mock<IGenericRepository<TicketParticipant>> _mockParticipantRepo = new();
    private readonly TicketGetByIdQueryHandler _handler;

    public TicketGetByIdQueryHandlerTests()
    {
        _mockUow.Setup(x => x.Tickets).Returns(_mockRepo.Object);
        _mockParticipantRepo.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<TicketParticipant>([]));
        _mockUow.Setup(x => x.TicketParticipants).Returns(_mockParticipantRepo.Object);
        _handler = new TicketGetByIdQueryHandler(
            _mockUow.Object,
            new TicketService.Infrastructure.Implements.Utils.SlaCalculator());
    }

    private static Ticket MakeTicket(Guid? customerId = null, Guid? PrimaryHandlerStaffId = null) => new()
    {
        Id = Guid.NewGuid(),
        Code = "T-001",
        BatteryAssetId = Guid.NewGuid(),
        CustomerId = customerId ?? Guid.NewGuid(),
        PrimaryHandlerStaffId = PrimaryHandlerStaffId,
        Title = "Test Ticket",
        Description = "desc",
        Category = TicketCategoryEnum.Other,
        Status = TicketStatusEnum.InProgress,
        Origin = TicketOriginEnum.ManualByCustomer,
        CreatedAt = DateTime.UtcNow,
        Activities = new List<TicketActivity>(),
        Chats = new List<TicketChat>(),
        MaintenanceLogs = new List<MaintenanceLog>()
    };

    private void SetupMock(List<Ticket> tickets)
        => _mockRepo.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<Ticket>(tickets));

    [Fact]
    public async Task Handle_AdminCanReadAnyTicket_Returns200()
    {
        var ticket = MakeTicket();
        SetupMock([ticket]);

        var result = await _handler.Handle(new TicketGetByIdQuery
        {
            Id = ticket.Id,
            ActorUserId = Guid.NewGuid(),
            ActorRoles = ["Admin"]
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.Data!.Id.Should().Be(ticket.Id.ToString());
    }

    [Fact]
    public async Task Handle_ManagerCanReadAnyTicket_Returns200()
    {
        var ticket = MakeTicket();
        SetupMock([ticket]);

        var result = await _handler.Handle(new TicketGetByIdQuery
        {
            Id = ticket.Id,
            ActorUserId = Guid.NewGuid(),
            ActorRoles = ["Manager"]
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Handle_CustomerCanReadOwnTicket_Returns200()
    {
        var customerId = Guid.NewGuid();
        var ticket = MakeTicket(customerId: customerId);
        SetupMock([ticket]);

        var result = await _handler.Handle(new TicketGetByIdQuery
        {
            Id = ticket.Id,
            ActorUserId = customerId,
            ActorRoles = ["Customer"]
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Handle_CustomerCannotReadOtherCustomerTicket_Returns403()
    {
        var ticket = MakeTicket(customerId: Guid.NewGuid());
        SetupMock([ticket]);

        var result = await _handler.Handle(new TicketGetByIdQuery
        {
            Id = ticket.Id,
            ActorUserId = Guid.NewGuid(),
            ActorRoles = ["Customer"]
        }, default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_StaffCanReadAssignedTicket_Returns200()
    {
        var staffId = Guid.NewGuid();
        var ticket = MakeTicket(PrimaryHandlerStaffId: staffId);
        SetupMock([ticket]);

        var result = await _handler.Handle(new TicketGetByIdQuery
        {
            Id = ticket.Id,
            ActorUserId = staffId,
            ActorRoles = ["Staff"]
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Handle_StaffCannotReadUnassignedTicket_Returns403()
    {
        var ticket = MakeTicket(PrimaryHandlerStaffId: Guid.NewGuid());
        SetupMock([ticket]);

        var result = await _handler.Handle(new TicketGetByIdQuery
        {
            Id = ticket.Id,
            ActorUserId = Guid.NewGuid(),
            ActorRoles = ["Staff"]
        }, default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_TicketNotFound_Returns404()
    {
        SetupMock([]);

        var result = await _handler.Handle(new TicketGetByIdQuery
        {
            Id = Guid.NewGuid(),
            ActorUserId = Guid.NewGuid(),
            ActorRoles = ["Admin"]
        }, default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_InternalChatsHiddenFromCustomer()
    {
        var customerId = Guid.NewGuid();
        var ticket = MakeTicket(customerId: customerId);
        ticket.Chats = new List<TicketChat>
        {
            new() { Id = Guid.NewGuid(), TicketId = ticket.Id, Ticket = ticket, AuthorUserId = Guid.NewGuid(),
                    AuthorRole = ActorRoleEnum.Staff, Body = "Internal note", IsInternal = true,
                    CreatedAt = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), TicketId = ticket.Id, Ticket = ticket, AuthorUserId = customerId,
                    AuthorRole = ActorRoleEnum.Customer, Body = "Public comment", IsInternal = false,
                    CreatedAt = DateTime.UtcNow }
        };
        SetupMock([ticket]);

        var result = await _handler.Handle(new TicketGetByIdQuery
        {
            Id = ticket.Id,
            ActorUserId = customerId,
            ActorRoles = ["Customer"]
        }, default);

        result.Data!.Chats.Should().HaveCount(1);
        result.Data.Chats[0].IsInternal.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_SlaTimerHiddenFromCustomer_ButExpectedCompletionExposed()
    {
        // GH-1242 — SLA là chỉ số nội bộ của Staff. Customer chỉ được biết ngày dự kiến
        // hoàn thành, không thấy BreachAt/WarningSentAt/RemainingPercent.
        var customerId = Guid.NewGuid();
        var ticket = MakeTicket(customerId: customerId);
        var dueAt = DateTime.UtcNow.AddDays(2);
        ticket.SlaTimers.Add(new SlaTimer
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            Type = SlaTimerTypeEnum.Resolution,
            Priority = TicketPriorityEnum.P3Normal,
            StartedAt = DateTime.UtcNow,
            DueAt = dueAt,
            OriginalDueAt = dueAt,
            Status = SlaTimerStatusEnum.Running
        });
        SetupMock([ticket]);

        var result = await _handler.Handle(new TicketGetByIdQuery
        {
            Id = ticket.Id,
            ActorUserId = customerId,
            ActorRoles = ["Customer"]
        }, default);

        result.Data!.ResponseSlaTimer.Should().BeNull();
        result.Data!.ResolutionSlaTimer.Should().BeNull();
        result.Data.ExpectedCompletionAtUtc.Should().Be(dueAt);
    }

    [Fact]
    public async Task Handle_SlaTimerVisibleToStaff_WithWorkingDayBudget()
    {
        var staffId = Guid.NewGuid();
        var ticket = MakeTicket(PrimaryHandlerStaffId: staffId);
        var dueAt = DateTime.UtcNow.AddDays(2);
        ticket.SlaTimers.Add(new SlaTimer
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            Type = SlaTimerTypeEnum.Resolution,
            Priority = TicketPriorityEnum.P3Normal,
            StartedAt = DateTime.UtcNow,
            DueAt = dueAt,
            OriginalDueAt = dueAt,
            Status = SlaTimerStatusEnum.Running
        });
        SetupMock([ticket]);

        var result = await _handler.Handle(new TicketGetByIdQuery
        {
            Id = ticket.Id,
            ActorUserId = staffId,
            ActorRoles = ["Staff"]
        }, default);

        result.Data!.ResolutionSlaTimer.Should().NotBeNull();
        result.Data.ResolutionSlaTimer!.SlaWorkingDays.Should().Be(2, "P3 = 2 ngày làm việc");
        result.Data.ResolutionSlaTimer.SlaWorkingHours.Should().Be(20, "2 ngày × 10h/ngày");
        result.Data.ResolutionSlaTimer.RemainingWorkingMinutes.Should().BePositive();
        result.Data.ExpectedCompletionAtUtc.Should().Be(dueAt);
    }
}
