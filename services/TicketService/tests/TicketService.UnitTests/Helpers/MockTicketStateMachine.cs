using Moq;
using TicketService.Application.StateMachine;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.UnitTests.Helpers;

public static class MockTicketStateMachine
{
    public static Mock<ITicketStateMachine> Create()
    {
        var mock = new Mock<ITicketStateMachine>();

        // Default behavior for ExecuteAsync: actually update the status
        mock.Setup(x => x.ExecuteAsync(It.IsAny<Ticket>(), It.IsAny<TicketStatusEnum>(), It.IsAny<TransitionContext>(), It.IsAny<CancellationToken>()))
            .Callback<Ticket, TicketStatusEnum, TransitionContext, CancellationToken>((ticket, target, ctx, ct) =>
            {
                ticket.Status = target;
                ticket.UpdatedAt = DateTime.UtcNow;
            })
            .ReturnsAsync(new TransitionResult { IsAllowed = true });

        // Default behavior for CanTransition: allow by default (tests can override)
        mock.Setup(x => x.CanTransition(It.IsAny<Ticket>(), It.IsAny<TicketStatusEnum>(), It.IsAny<ActorRoleEnum>(), It.IsAny<Guid>()))
            .Returns(new TransitionResult { IsAllowed = true });

        return mock;
    }
}
