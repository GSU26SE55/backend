using SharedKernels.Domain;
using TicketService.Domain.Enums;

namespace TicketService.Domain.Entities;

public class SlaPauseEvent : AuditableEntity
{
    public Guid SlaTimerId { get; set; }
    public PauseReasonEnum Reason { get; set; }
    public string? Note { get; set; }
    public DateTime PausedAt { get; set; }
    public Guid PausedByUserId { get; set; }
    public DateTime? ResumedAt { get; set; }
    public Guid? ResumedByUserId { get; set; }
    public int? DurationMinutes { get; set; }

    // Advanced fields (§33.4)
    public bool? IsApprovedByManager { get; set; }
    public Guid? ApprovedByManagerId { get; set; }
    public short? AutoResumeReason { get; set; } // TimeLimitExceeded=1, CustomerTimeout=2, ManagerForce=3

    public SlaTimer SlaTimer { get; set; } = null!;
}
