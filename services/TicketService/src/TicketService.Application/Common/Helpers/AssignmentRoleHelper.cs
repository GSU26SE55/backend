using TicketService.Domain.Enums;

namespace TicketService.Application.Common.Helpers;

public static class AssignmentRoleHelper
{
    /// <summary>
    /// Validates that the PrimaryHandler's tier meets the minimum tier required by the ticket's priority.
    /// P3Normal → Generalist+, P2High → ModuleSpecialist+, P1Critical → SeniorSpecialist.
    /// </summary>
    public static bool ValidatePrimaryHandlerTier(TicketPriorityEnum priority, StaffSkillTierEnum tier)
    {
        return priority switch
        {
            TicketPriorityEnum.P1Critical => tier >= StaffSkillTierEnum.SeniorSpecialist,
            TicketPriorityEnum.P2High => tier >= StaffSkillTierEnum.ModuleSpecialist,
            TicketPriorityEnum.P3Normal => tier >= StaffSkillTierEnum.Generalist,
            _ => false
        };
    }

    public static string GetTierRequirementMessage(TicketPriorityEnum priority) => priority switch
    {
        TicketPriorityEnum.P1Critical => "SeniorSpecialist (Tier 3)",
        TicketPriorityEnum.P2High => "ModuleSpecialist (Tier 2) trở lên",
        TicketPriorityEnum.P3Normal => "Generalist (Tier 1) trở lên",
        _ => "không xác định"
    };
}
