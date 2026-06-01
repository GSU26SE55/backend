using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.StateMachine.Rules;

public interface ITransitionRuleProvider
{
    Dictionary<TicketStatusEnum, Dictionary<TicketStatusEnum,
        Func<Ticket, ActorRoleEnum, Guid, TransitionResult>>> GetRules();
}
