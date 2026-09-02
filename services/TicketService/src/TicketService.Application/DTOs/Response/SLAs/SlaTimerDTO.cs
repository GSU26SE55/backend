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

    /// <summary>Budget SLA theo số ngày làm việc của priority (P1=14 · P2=3 · P3=2).</summary>
    public int SlaWorkingDays { get; set; }

    /// <summary>
    /// Budget SLA quy ra giờ làm việc (P1=140h · P2=30h · P3=20h) — để FE không phải tự nhân
    /// số ngày với độ dài cửa sổ làm việc.
    /// </summary>
    public int SlaWorkingHours { get; set; }

    /// <summary>Số phút làm việc còn lại tới <see cref="DueAt"/> — đồng hồ đếm ngược của Staff.</summary>
    public int RemainingWorkingMinutes { get; set; }

    /// <summary>
    /// Số phút SLA calendar (ngày lễ/nghỉ) đã cộng thêm vào <see cref="DueAt"/> so với deadline
    /// nếu không có ngày nghỉ nào rơi vào khoảng chạy của timer. 0 nếu không có ngày nào bị loại.
    /// </summary>
    public int CalendarExtensionMinutes { get; set; }

    /// <summary>Các ngày (local date) trong <c>SlaNonWorkingPeriod</c> rơi vào khoảng chạy của timer.</summary>
    public List<DateOnly> CalendarExtensionDays { get; set; } = [];

    /// <summary>
    /// Đồng hồ tiếp cứu nội bộ — phút làm việc còn lại trong hạn mức 24h (1440 phút) của Staff mới.
    /// Chỉ có giá trị khi Status == Breached và ticket đang InProgress.
    /// </summary>
    public int? RescueRemainingMinutes { get; set; }
}
