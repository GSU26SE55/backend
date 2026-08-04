using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.Participants;
using TicketService.Application.CQRS.Handler.Participants;
using TicketService.Application.Interfaces.Services;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Handlers.Participants;

public class ParticipantRemoveCommandHandlerTests
{
    [Fact]
    public async Task Handle_ManagerRemovesParticipant_WritesActivity()
    {
        var ticket = new Ticket { Id = Guid.NewGuid(), Code = "TKT-001", Title = "Test", Description = "Test" };
        var participant = new TicketParticipant { Id = Guid.NewGuid(), TicketId = ticket.Id, Ticket = ticket, UserId = Guid.NewGuid(), UserRole = ActorRoleEnum.Customer, ParticipantType = ParticipantTypeEnum.Collaborator, AddedByUserId = Guid.NewGuid(), AddedAt = DateTime.UtcNow };
        var activity = new Mock<IActivityLogger>();
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { ticket }, participantSeed: new[] { participant });
        var command = new ParticipantRemoveCommand { TicketId = ticket.Id, UserId = participant.UserId, ActorUserId = Guid.NewGuid(), ActorRole = ActorRoleEnum.Manager, RemoveReason = "No longer needed" };

        var handler = new ParticipantRemoveCommandHandler(uow.Object, Mock.Of<IIntegrationEventOutboxWriter>(), activity.Object, Mock.Of<ITicketChatRealtimeNotifier>(), NullLogger<ParticipantRemoveCommandHandler>.Instance);
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        activity.Verify(x => x.LogAsync(ticket.Id, command.ActorUserId, command.ActorRole, null, ActivityActionEnum.ParticipantRemoved, ParticipantTypeEnum.Collaborator.ToString(), It.IsAny<string?>(), command.RemoveReason), Times.Once);
    }
}
