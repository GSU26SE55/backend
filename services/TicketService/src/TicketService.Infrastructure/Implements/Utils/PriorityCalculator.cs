using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Enums;

namespace TicketService.Infrastructure.Implements.Utils;

public class PriorityCalculator : IPriorityCalculator
{
    public TicketPriorityEnum Calculate(ImpactScopeEnum impact, UrgencyLevelEnum urgency)
    {
        // Matrix lookup based on overall.md §2.4bis

        // MultiSite (3)
        if (impact == ImpactScopeEnum.MultiSite)
        {
            if (urgency >= UrgencyLevelEnum.Medium)
                return TicketPriorityEnum.P1Critical;
            return TicketPriorityEnum.P2High;
        }

        // Site (2)
        if (impact == ImpactScopeEnum.Site)
        {
            if (urgency == UrgencyLevelEnum.High)
                return TicketPriorityEnum.P1Critical;
            return TicketPriorityEnum.P2High;
        }

        // SingleAsset (1)
        if (impact == ImpactScopeEnum.SingleAsset)
        {
            if (urgency == UrgencyLevelEnum.High)
                return TicketPriorityEnum.P2High;
            return TicketPriorityEnum.P3Normal;
        }

        return TicketPriorityEnum.P3Normal;
    }
}
