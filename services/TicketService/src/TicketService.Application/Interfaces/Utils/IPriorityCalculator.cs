using TicketService.Domain.Enums;

namespace TicketService.Application.Interfaces.Utils;

public interface IPriorityCalculator
{
    TicketPriorityEnum Calculate(ImpactScopeEnum impact, UrgencyLevelEnum urgency);
}
