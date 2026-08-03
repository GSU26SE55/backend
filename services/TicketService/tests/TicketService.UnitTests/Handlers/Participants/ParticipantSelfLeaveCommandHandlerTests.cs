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

public class ParticipantSelfLeaveCommandHandlerTests
{
    [Fact]
    public async Task Handle_WatcherLeaves_WritesActivity()
    {
        var ticket = new Ticket { Id = Guid.NewGuid(), Code = "TKT-001", Title = "Test", Description = "Test" };
        var participant = new TicketParticipant { Id = Guid.NewGuid(), TicketId = ticket.Id, Ticket = ticket, UserId = Guid.NewGuid(), UserRole = ActorRoleEnum.Customer, ParticipantType = ParticipantTypeEnum.Watcher, AddedByUserId = Guid.NewGuid(), AddedAt = DateTime.UtcNow };
        var activity = new Mock<IActivityLogger>();
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { ticket }, participantSeed: new[] { participant });

        var result = await new ParticipantSelfLeaveCommandHandler(uow.Object, Mock.Of<IIntegrationEventOutboxWriter>(), activity.Object)
            .Handle(new ParticipantSelfLeaveCommand { TicketId = ticket.Id, ActorUserId = participant.UserId, LeaveReason = "Done" }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        activity.Verify(x => x.LogAsync(ticket.Id, participant.UserId, participant.UserRole, null, ActivityActionEnum.ParticipantRemoved, ParticipantTypeEnum.Watcher.ToString(), It.IsAny<string?>(), "Done"), Times.Once);
    }
}
