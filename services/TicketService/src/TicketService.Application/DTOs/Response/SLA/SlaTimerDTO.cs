using TicketService.Domain.Enums;

namespace TicketService.Application.DTOs.Response.SLA;

public class SlaTimerDTO
{
    public string Id { get; set; } = string.Empty;
    public TicketPriorityEnum Priority { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime DueAt { get; set; }
    public DateTime OriginalDueAt { get; set; }
    public int TotalPausedMinutes { get; set; }
    public DateTime? WarningSentAt { get; set; }
    public DateTime? BreachAt { get; set; }
    public SlaTimerStatusEnum Status { get; set; }
    public double RemainingPercent { get; set; }
}
