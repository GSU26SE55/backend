using SharedKernels.Domain;
using TicketService.Domain.Enums;

namespace TicketService.Domain.Entities;

public class SlaTimer : AuditableEntity
{
    public Guid TicketId { get; set; }
    public TicketPriorityEnum Priority { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime DueAt { get; set; }
    public DateTime OriginalDueAt { get; set; }
    public int TotalPausedMinutes { get; set; }
    public DateTime? CurrentPauseStartedAt { get; set; }
    public DateTime? WarningSentAt { get; set; }
    public DateTime? BreachAt { get; set; }
    public SlaTimerStatusEnum Status { get; set; }

    // Advanced SLA fields (§33.3)
    public int MaxTotalPauseMinutes { get; set; }
    public int MaxPauseEpisodes { get; set; }
    public int PauseEpisodesCount { get; set; }
    public DateTime? LastAutoResumeAt { get; set; }
    public bool ApprovalRequired { get; set; }

    public Ticket Ticket { get; set; } = null!;
}
