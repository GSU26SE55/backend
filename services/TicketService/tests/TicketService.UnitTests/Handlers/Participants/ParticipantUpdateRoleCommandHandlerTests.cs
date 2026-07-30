using FluentAssertions;
using Moq;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.Participants;
using TicketService.Application.CQRS.Handler.Participants;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Handlers.Participants;

public class ParticipantUpdateRoleCommandHandlerTests
{
    [Fact]
    public async Task Handle_ChangesRole_WritesActivity()
    {
        var ticket = new Ticket { Id = Guid.NewGuid(), Code = "TKT-001", Title = "Test", Description = "Test" };
        var participant = new TicketParticipant { Id = Guid.NewGuid(), TicketId = ticket.Id, Ticket = ticket, UserId = Guid.NewGuid(), UserRole = ActorRoleEnum.Customer, ParticipantType = ParticipantTypeEnum.Collaborator, AddedByUserId = Guid.NewGuid(), AddedAt = DateTime.UtcNow };
        var outbox = new Mock<IIntegrationEventOutboxWriter>();
        var activity = new Mock<IActivityLogger>();
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { ticket }, participantSeed: new[] { participant });
        var command = new ParticipantUpdateRoleCommand { TicketId = ticket.Id, UserId = participant.UserId, ParticipantType = ParticipantTypeEnum.Watcher, ActorUserId = Guid.NewGuid(), ActorRole = ActorRoleEnum.Manager };

        var result = await new ParticipantUpdateRoleCommandHandler(uow.Object, outbox.Object, activity.Object).Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        activity.Verify(x => x.LogAsync(ticket.Id, command.ActorUserId, command.ActorRole, null, ActivityActionEnum.ParticipantRoleChanged, ParticipantTypeEnum.Collaborator.ToString(), It.IsAny<string?>(), null), Times.Once);
    }
}
