namespace BatteryService.Application.DTOs;

/// <summary>Một mốc bảo trì định kỳ của pin — nhật ký ở tầng tài sản.</summary>
public class MaintenanceCycleDto
{
    public string Id { get; set; } = string.Empty;

    public string BatteryAssetId { get; set; } = string.Empty;

    public int CycleNo { get; set; }

    /// <summary>Hạn theo kế hoạch của kỳ này.</summary>
    public DateTime DueAtUtc { get; set; }

    /// <summary>Thời điểm hệ thống ghi mốc.</summary>
    public DateTime RecordedAtUtc { get; set; }

    /// <summary>SoH (%) tại mốc này — mốc so sánh sức khoẻ giữa các kỳ.</summary>
    public decimal? SohPercentAtCycle { get; set; }

    /// <summary>
    /// Ticket bảo trì mở cho kỳ này, hoặc <c>null</c> nếu chưa gắn được. FE dùng để mở
    /// thẳng trang chi tiết ticket từ dòng nhật ký; null thì không hiện liên kết.
    /// </summary>
    public string? TicketId { get; set; }

    // ── Tình trạng pin trong kỳ vừa qua (chụp lúc ghi mốc) ───────────────────
    // Tất cả nullable: pin mất kết nối cả kỳ thì không có gì để tổng hợp.

    public decimal? AvgTemperatureCelsius { get; set; }
    public decimal? MaxTemperatureCelsius { get; set; }
    public decimal? MinVoltage { get; set; }
    public decimal? MaxVoltage { get; set; }
    public int? CycleCountDelta { get; set; }
    public int? AlertCount { get; set; }
    public int? CriticalAlertCount { get; set; }

    /// <summary>Số bản ghi cảm biến dùng để tổng hợp — 0 nghĩa là pin mất kết nối cả kỳ.</summary>
    public int? ReadingCount { get; set; }
}
