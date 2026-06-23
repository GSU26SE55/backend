using TicketService.Application.CQRS.Handler.Participants;
using TicketService.Application.CQRS.Query.TicketParticipants;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Helpers;

namespace TicketService.UnitTests.Queries;

public class TicketParticipantsQueryHandlerTests
{
    private static Ticket MakeTicket(Guid? customerId = null, Guid? assignedStaffId = null) => new()
    {
        Id = Guid.NewGuid(),
        Code = "T-001",
        CustomerId = customerId ?? Guid.NewGuid(),
        AssignedStaffId = assignedStaffId,
        Title = "Test",
        Description = "desc",
        Category = TicketCategoryEnum.Other,
        Status = TicketStatusEnum.InProgress,
        Origin = TicketOriginEnum.ManualByCustomer,
        CreatedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task Handle_AdminViewsActiveParticipants_Returns200()
    {
        var ticket = MakeTicket();
        var participant = new TicketParticipant
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            Ticket = ticket,
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Customer,
            ParticipantType = ParticipantTypeEnum.Owner,
            CanPost = true,
            CanViewInternal = false,
            AddedByUserId = Guid.NewGuid(),
            AddedAt = DateTime.UtcNow
        };

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket }, participantSeed: new[] { participant });

        var handler = new TicketParticipantsQueryHandler(uow.Object);

        var result = await handler.Handle(new TicketParticipantsQuery
        {
            TicketId = ticket.Id,
            ActorUserId = Guid.NewGuid(),
            ActorRoles = ["Admin"]
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.Data.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_ActiveParticipantCanViewWithoutBeingCustomerOrStaff_Returns200()
    {
        var ticket = MakeTicket();
        var collaboratorId = Guid.NewGuid();
        var participant = new TicketParticipant
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            Ticket = ticket,
            UserId = collaboratorId,
            UserRole = ActorRoleEnum.Staff,
            ParticipantType = ParticipantTypeEnum.Collaborator,
            CanPost = true,
            CanViewInternal = true,
            AddedByUserId = Guid.NewGuid(),
            AddedAt = DateTime.UtcNow
        };

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket }, participantSeed: new[] { participant });

        var handler = new TicketParticipantsQueryHandler(uow.Object);

        var result = await handler.Handle(new TicketParticipantsQuery
        {
            TicketId = ticket.Id,
            ActorUserId = collaboratorId,
            ActorRoles = ["Staff"]
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Handle_NoAccessNoActiveParticipant_Returns403()
    {
        var ticket = MakeTicket();

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { ticket });

        var handler = new TicketParticipantsQueryHandler(uow.Object);

        var result = await handler.Handle(new TicketParticipantsQuery
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
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended();

        var handler = new TicketParticipantsQueryHandler(uow.Object);

        var result = await handler.Handle(new TicketParticipantsQuery
        {
            TicketId = Guid.NewGuid(),
            ActorUserId = Guid.NewGuid(),
            ActorRoles = ["Admin"]
        }, default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }
}
