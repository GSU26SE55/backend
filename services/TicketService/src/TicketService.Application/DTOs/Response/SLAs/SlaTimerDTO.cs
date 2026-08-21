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

    /// <summary>Budget SLA theo số ngày làm việc của priority (P1=1 · P2=3 · P3=7).</summary>
    public int SlaWorkingDays { get; set; }

    /// <summary>
    /// Budget SLA quy ra giờ làm việc (P1=10h · P2=30h · P3=70h) — để FE không phải tự nhân
    /// số ngày với độ dài cửa sổ làm việc.
    /// </summary>
    public int SlaWorkingHours { get; set; }

    /// <summary>Số phút làm việc còn lại tới <see cref="DueAt"/> — đồng hồ đếm ngược của Staff.</summary>
    public int RemainingWorkingMinutes { get; set; }
}
