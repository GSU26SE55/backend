using TicketService.Domain.Enums;

namespace TicketService.Application.Interfaces.Helpers;

public interface IPriorityCalculator
{
    TicketPriorityEnum Calculate(ImpactScopeEnum impact, UrgencyLevelEnum urgency);
}
