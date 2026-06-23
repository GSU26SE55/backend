using Moq;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.ParticipantSelfLeave;
using TicketService.Application.CQRS.Handler.Participants;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Helpers;

namespace TicketService.UnitTests.Handlers.Participants;

public class ParticipantSelfLeaveCommandHandlerTests
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
    public async Task Handle_WatcherLeaves_Returns200()
    {
        var ticketId = Guid.NewGuid();
        var watcherId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);
        var participant = MakeParticipant(ticket, watcherId, ParticipantTypeEnum.Watcher);

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, participants) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket }, participantSeed: new[] { participant });

        var command = new ParticipantSelfLeaveCommand
        {
            TicketId = ticketId,
            LeaveReason = "no longer relevant",
            ActorUserId = watcherId
        };

        var handler = new ParticipantSelfLeaveCommandHandler(uow.Object, _producer.Object);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        participant.RemovedAt.Should().NotBeNull();
        participants.Verify(x => x.UpdateAsync(participant), Times.Once);
        _producer.Verify(x => x.PublishAsync(It.IsAny<SharedContracts.Events.Chats.ParticipantRemovedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_OwnerCannotSelfLeave_Returns403()
    {
        var ticketId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);
        var participant = MakeParticipant(ticket, ownerId, ParticipantTypeEnum.Owner);

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket }, participantSeed: new[] { participant });

        var command = new ParticipantSelfLeaveCommand
        {
            TicketId = ticketId,
            ActorUserId = ownerId
        };

        var handler = new ParticipantSelfLeaveCommandHandler(uow.Object, _producer.Object);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_NoActiveParticipant_Returns404()
    {
        var ticketId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { ticket });

        var command = new ParticipantSelfLeaveCommand
        {
            TicketId = ticketId,
            ActorUserId = Guid.NewGuid()
        };

        var handler = new ParticipantSelfLeaveCommandHandler(uow.Object, _producer.Object);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }
}
