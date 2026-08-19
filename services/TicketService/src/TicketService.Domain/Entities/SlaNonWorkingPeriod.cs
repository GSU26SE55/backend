using SharedKernels.Domain;

namespace TicketService.Domain.Entities;

public class SlaNonWorkingPeriod : AuditableEntity
{
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string Reason { get; set; } = string.Empty;
}
