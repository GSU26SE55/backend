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

public class ParticipantAddCommandHandlerTests
{
    [Fact]
    public async Task Handle_ManagerAddsParticipant_WritesOutboxAndActivity()
    {
        var ticket = new Ticket { Id = Guid.NewGuid(), Code = "TKT-001", Title = "Test", Description = "Test" };
        var outbox = new Mock<IIntegrationEventOutboxWriter>();
        var activity = new Mock<IActivityLogger>();
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, participants) = MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { ticket });
        var command = new ParticipantAddCommand { TicketId = ticket.Id, UserId = Guid.NewGuid(), UserRole = ActorRoleEnum.Customer, ParticipantType = ParticipantTypeEnum.Watcher, ActorUserId = Guid.NewGuid(), ActorRole = ActorRoleEnum.Manager };

        var result = await new ParticipantAddCommandHandler(uow.Object, outbox.Object, activity.Object).Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        participants.Verify(x => x.AddAsync(It.IsAny<TicketParticipant>()), Times.Once);
        outbox.Verify(x => x.WriteAsync(It.IsAny<SharedContracts.Events.Chats.ParticipantAddedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        activity.Verify(x => x.LogAsync(ticket.Id, command.ActorUserId, command.ActorRole, null, ActivityActionEnum.ParticipantAdded, null, It.IsAny<string?>(), null), Times.Once);
    }
}
