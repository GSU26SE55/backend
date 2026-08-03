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

public class ParticipantBulkAddCommandHandlerTests
{
    [Fact]
    public async Task Handle_BulkAdd_WritesAnActivityPerParticipant()
    {
        var ticket = new Ticket { Id = Guid.NewGuid(), Code = "TKT-001", Title = "Test", Description = "Test" };
        var activity = new Mock<IActivityLogger>();
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { ticket });
        var command = new ParticipantBulkAddCommand { TicketId = ticket.Id, ActorUserId = Guid.NewGuid(), ActorRole = ActorRoleEnum.Manager, Participants = new() { new(Guid.NewGuid(), ActorRoleEnum.Customer, ParticipantTypeEnum.Watcher, true, false), new(Guid.NewGuid(), ActorRoleEnum.Staff, ParticipantTypeEnum.Collaborator, true, true) } };

        var result = await new ParticipantBulkAddCommandHandler(uow.Object, Mock.Of<IIntegrationEventOutboxWriter>(), activity.Object).Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        activity.Verify(x => x.LogAsync(ticket.Id, command.ActorUserId, command.ActorRole, null, ActivityActionEnum.ParticipantAdded, null, It.IsAny<string?>(), null), Times.Exactly(2));
    }
}
