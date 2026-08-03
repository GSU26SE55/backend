using BatteryService.Domain.Enums;

namespace BatteryService.Application.DTOs;

public class AlertDto
{
    /// <summary>Định danh resource.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// ID BatteryAsset (Guid). **Chuỗi rỗng `""`** cho alert cấp SITE (ambient NS-21 /
    /// environmental incident) — không gắn pin cụ thể; khi đó dùng <see cref="SiteId"/>.
    /// </summary>
    public string BatteryAssetId { get; set; } = string.Empty;

    /// <summary>
    /// Sprint Bonus NS-21 (#661) — ID Site cho alert cấp site (ambient 9/10/11, environmental
    /// incident 14). Null cho alert cấp pin thông thường. Giúp FE route alert về đúng site.
    /// </summary>
    public string? SiteId { get; set; }

    /// <summary>Serial của battery liên quan. Rỗng cho alert cấp site.</summary>
    public string BatterySerialNumber { get; set; } = string.Empty;

    /// <summary>Loại bất thường (xem AnomalyTypeEnum 1..16).</summary>
    public AnomalyTypeEnum AnomalyType { get; set; }

    /// <summary>Severity của alert (Warning | Critical).</summary>
    public AlertSeverityEnum Severity { get; set; }

    /// <summary>Giá trị ngưỡng đã vi phạm.</summary>
    public decimal? ThresholdValue { get; set; }

    /// <summary>Giá trị thực tế đo được.</summary>
    public decimal? ActualValue { get; set; }

    /// <summary>Đơn vị đo (V | A | °C | %).</summary>
    public string? Unit { get; set; }

    /// <summary>Timestamp phát hiện (UTC).</summary>
    public DateTime DetectedAt { get; set; }

    /// <summary>Filter theo status enum.</summary>
    public AlertStatusEnum Status { get; set; }

    /// <summary>ID Ticket liên kết (nullable nếu chưa Saga link).</summary>
    public string? TicketId { get; set; }

    /// <summary>ID user đã acknowledge.</summary>
    public string? AcknowledgedByUserId { get; set; }

    /// <summary>Timestamp acknowledged (UTC).</summary>
    public DateTime? AcknowledgedAt { get; set; }

    /// <summary>Timestamp resolved (UTC).</summary>
    public DateTime? ResolvedAt { get; set; }

    /// <summary>Timestamp kết thúc cửa sổ dedup alert.</summary>
    public DateTime DedupWindowEndUtc { get; set; }

    /// <summary>
    /// GH-805 — bằng chứng AI khiến alert nổ (JSON: <c>risk_level</c>, <c>priority</c>,
    /// <c>action_code</c>, <c>warnings[]</c>). Null cho alert sinh từ threshold rule.
    /// </summary>
    public string? AiEvidence { get; set; }

    /// <summary>Timestamp tạo (UTC).</summary>
    public DateTime CreatedAt { get; set; }
}
