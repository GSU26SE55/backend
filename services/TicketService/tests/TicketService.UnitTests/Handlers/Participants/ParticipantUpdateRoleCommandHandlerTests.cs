using Moq;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.Participants;
using TicketService.Application.CQRS.Handler.Participants;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Helpers;

namespace TicketService.UnitTests.Handlers.Participants;

public class ParticipantUpdateRoleCommandHandlerTests
{
    private readonly Mock<IMessageProducerService> _producer = new();

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
    public async Task Handle_ManagerChangesCollaboratorToWatcher_Returns200()
    {
        var ticketId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);
        var participant = MakeParticipant(ticket, targetUserId, ParticipantTypeEnum.Collaborator);

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, participants) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket }, participantSeed: new[] { participant });

        var command = new ParticipantUpdateRoleCommand
        {
            TicketId = ticketId,
            UserId = targetUserId,
            ParticipantType = ParticipantTypeEnum.Watcher,
            ActorUserId = managerId,
            ActorRole = ActorRoleEnum.Manager
        };

        var handler = new ParticipantUpdateRoleCommandHandler(uow.Object, _producer.Object);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        participant.ParticipantType.Should().Be(ParticipantTypeEnum.Watcher);

        participants.Verify(x => x.UpdateAsync(participant), Times.Once);
        _producer.Verify(x => x.PublishAsync(It.IsAny<SharedContracts.Events.Chats.ParticipantRoleChangedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
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

        var command = new ParticipantUpdateRoleCommand
        {
            TicketId = ticketId,
            UserId = targetUserId,
            ParticipantType = ParticipantTypeEnum.Watcher,
            ActorUserId = staffId,
            ActorRole = ActorRoleEnum.Staff
        };

        var handler = new ParticipantUpdateRoleCommandHandler(uow.Object, _producer.Object);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_TargetIsOwner_Returns403()
    {
        var ticketId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);
        var participant = MakeParticipant(ticket, ownerId, ParticipantTypeEnum.Owner);

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket }, participantSeed: new[] { participant });

        var command = new ParticipantUpdateRoleCommand
        {
            TicketId = ticketId,
            UserId = ownerId,
            ParticipantType = ParticipantTypeEnum.Watcher,
            ActorUserId = managerId,
            ActorRole = ActorRoleEnum.Manager
        };

        var handler = new ParticipantUpdateRoleCommandHandler(uow.Object, _producer.Object);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_ParticipantNotFound_Returns404()
    {
        var ticketId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { ticket });

        var command = new ParticipantUpdateRoleCommand
        {
            TicketId = ticketId,
            UserId = Guid.NewGuid(),
            ParticipantType = ParticipantTypeEnum.Watcher,
            ActorUserId = managerId,
            ActorRole = ActorRoleEnum.Manager
        };

        var handler = new ParticipantUpdateRoleCommandHandler(uow.Object, _producer.Object);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Validate_InvalidParticipantType_ReturnsError()
    {
        var command = new ParticipantUpdateRoleCommand
        {
            TicketId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            ParticipantType = ParticipantTypeEnum.PrimaryAssignee
        };

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(e => e.Field == "ParticipantType");
    }
}
