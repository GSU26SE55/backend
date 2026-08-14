using TicketService.Domain.Enums;

namespace TicketService.Application.DTOs.Response.SLA;

public class SlaTimerDTO
{
    /// <summary>
    /// Id.
    /// </summary>
    public string Id { get; set; } = string.Empty;
    public TicketPriorityEnum Priority { get; set; }
    public DateTime StartedAt { get; set; }
    /// <summary>
    /// Due at.
    /// </summary>
    public DateTime DueAt { get; set; }
    public DateTime OriginalDueAt { get; set; }
    public int TotalPausedMinutes { get; set; }
    public int PauseEpisodesCount { get; set; }
    /// <summary>
    /// Warning sent at.
    /// </summary>
    public DateTime? WarningSentAt { get; set; }
    public DateTime? BreachAt { get; set; }
    public SlaTimerStatusEnum Status { get; set; }
    /// <summary>
    /// Remaining percent.
    /// </summary>
    public double RemainingPercent { get; set; }
}
