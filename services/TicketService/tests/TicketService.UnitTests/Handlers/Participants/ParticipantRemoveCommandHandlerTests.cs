using Microsoft.Extensions.Logging;
using Moq;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.ParticipantRemove;
using TicketService.Application.CQRS.Handler.Participants;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Helpers;

namespace TicketService.UnitTests.Handlers.Participants;

public class ParticipantRemoveCommandHandlerTests
{
    private readonly Mock<IMessageProducerService> _producer = new();
    private readonly Mock<ILogger<ParticipantRemoveCommandHandler>> _logger = new();

    private static Ticket MakeTicket(Guid ticketId) => new()
    {
        Id = ticketId,
        Code = "TKT-001",
        Title = "Test Ticket",
        Description = "Test Description"
    };

    private static TicketParticipant MakeParticipant(Ticket ticket, Guid userId, ParticipantTypeEnum type) => new()
    {
        Id = Guid.NewGuid(),
        TicketId = ticket.Id,
        Ticket = ticket,
        UserId = userId,
        UserRole = ActorRoleEnum.Customer,
        ParticipantType = type,
        CanPost = true,
        CanViewInternal = false,
        AddedByUserId = Guid.NewGuid(),
        AddedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task Handle_ManagerRemovesCollaborator_Returns200()
    {
        var ticketId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);
        var participant = MakeParticipant(ticket, targetUserId, ParticipantTypeEnum.Collaborator);

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, participants) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket }, participantSeed: new[] { participant });

        var command = new ParticipantRemoveCommand
        {
            TicketId = ticketId,
            UserId = targetUserId,
            ActorUserId = managerId,
            ActorRole = ActorRoleEnum.Manager
        };

        var handler = new ParticipantRemoveCommandHandler(uow.Object, _producer.Object, _logger.Object);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        participant.RemovedAt.Should().NotBeNull();
        participant.RemovedByUserId.Should().Be(managerId);

        participants.Verify(x => x.UpdateAsync(participant), Times.Once);
        _producer.Verify(x => x.PublishAsync(It.IsAny<SharedContracts.Events.Chats.ParticipantRemovedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ManagerRemovesOwner_Returns403()
    {
        var ticketId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);
        var participant = MakeParticipant(ticket, ownerId, ParticipantTypeEnum.Owner);

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket }, participantSeed: new[] { participant });

        var command = new ParticipantRemoveCommand
        {
            TicketId = ticketId,
            UserId = ownerId,
            RemoveReason = "test reason",
            ActorUserId = managerId,
            ActorRole = ActorRoleEnum.Manager
        };

        var handler = new ParticipantRemoveCommandHandler(uow.Object, _producer.Object, _logger.Object);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_AdminRemovesOwnerWithoutReason_Returns400()
    {
        var ticketId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);
        var participant = MakeParticipant(ticket, ownerId, ParticipantTypeEnum.Owner);

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket }, participantSeed: new[] { participant });

        var command = new ParticipantRemoveCommand
        {
            TicketId = ticketId,
            UserId = ownerId,
            ActorUserId = adminId,
            ActorRole = ActorRoleEnum.Admin
        };

        var handler = new ParticipantRemoveCommandHandler(uow.Object, _producer.Object, _logger.Object);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Handle_AdminRemovesOwnerWithReason_Returns200()
    {
        var ticketId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);
        var participant = MakeParticipant(ticket, ownerId, ParticipantTypeEnum.Owner);

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket }, participantSeed: new[] { participant });

        var command = new ParticipantRemoveCommand
        {
            TicketId = ticketId,
            UserId = ownerId,
            RemoveReason = "safety reason",
            ActorUserId = adminId,
            ActorRole = ActorRoleEnum.Admin
        };

        var handler = new ParticipantRemoveCommandHandler(uow.Object, _producer.Object, _logger.Object);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Handle_NonManagerActor_Returns403()
    {
        var ticketId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);
        var participant = MakeParticipant(ticket, targetUserId, ParticipantTypeEnum.Collaborator);

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket }, participantSeed: new[] { participant });

        var command = new ParticipantRemoveCommand
        {
            TicketId = ticketId,
            UserId = targetUserId,
            ActorUserId = staffId,
            ActorRole = ActorRoleEnum.Staff
        };

        var handler = new ParticipantRemoveCommandHandler(uow.Object, _producer.Object, _logger.Object);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_ParticipantNotFound_Returns404()
    {
        var ticketId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { ticket });

        var command = new ParticipantRemoveCommand
        {
            TicketId = ticketId,
            UserId = Guid.NewGuid(),
            ActorUserId = Guid.NewGuid(),
            ActorRole = ActorRoleEnum.Manager
        };

        var handler = new ParticipantRemoveCommandHandler(uow.Object, _producer.Object, _logger.Object);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }
}
