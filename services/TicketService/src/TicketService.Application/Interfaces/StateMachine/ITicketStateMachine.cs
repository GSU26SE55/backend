using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.StateMachine;

public interface ITicketStateMachine
{
    TransitionResult CanTransition(Ticket ticket, TicketStatusEnum target, ActorRoleEnum actorRole, Guid actorUserId);
    Task<TransitionResult> ExecuteAsync(Ticket ticket, TicketStatusEnum target, TransitionContext ctx, CancellationToken ct);
}
