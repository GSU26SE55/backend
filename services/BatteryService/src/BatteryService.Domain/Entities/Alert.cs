using BatteryService.Domain.Enums;
using SharedKernels.Domain;

namespace BatteryService.Domain.Entities;

public class Alert : AuditableEntity
{
    /// <summary>
    /// Sprint 5B #100 — nullable: site-level alert (EnvironmentalIncident) không có asset.
    /// </summary>
    public Guid? BatteryAssetId { get; set; }

    /// <summary>
    /// Sprint 5B #100 — site-level alert (EnvironmentalIncident) đặt SiteId.
    /// Battery-level alert có thể leave null hoặc populate từ BatteryAsset.SiteId.
    /// </summary>
    public Guid? SiteId { get; set; }

    /// <summary>
    /// Sprint 5B #100 — link tới EnvironmentalIncident nếu alert được sinh
    /// từ site-level incident.
    /// </summary>
    public Guid? EnvironmentalIncidentId { get; set; }

    public AnomalyTypeEnum AnomalyType { get; set; }

    public AlertSeverityEnum Severity { get; set; }

    /// <summary>§1.3.5 — nullable: incident-based alert (smoke/water) không có threshold.</summary>
    public decimal? ThresholdValue { get; set; }

    /// <summary>§1.3.5 — nullable: incident-based alert không có measured value.</summary>
    public decimal? ActualValue { get; set; }

    /// <summary>§1.3.5 — nullable: incident-based alert không có unit.</summary>
    public string? Unit { get; set; }

    public DateTime DetectedAt { get; set; }

    public AlertStatusEnum Status { get; set; } = AlertStatusEnum.Open;

    public Guid? MergedIntoAlertId { get; set; }

    public Guid? TicketId { get; set; }

    public Guid? AcknowledgedByUserId { get; set; }

    public DateTime? AcknowledgedAt { get; set; }

    public DateTime? ResolvedAt { get; set; }

    public DateTime DedupWindowEndUtc { get; set; }

    /// <summary>
    /// GH-805 — bằng chứng AI khiến alert nổ, JSON gọn:
    /// <c>risk_level</c>, <c>priority</c>, <c>action_code</c>, <c>warnings[]</c>.
    /// Null cho alert sinh từ threshold rule (không có dữ liệu AI).
    /// Cần thiết vì alert có thể nổ do <c>risk.priority</c> P1/P2 trong khi classification vẫn Normal —
    /// không có field này thì không thể biết vì sao alert được tạo.
    /// </summary>
    public string? AiEvidence { get; set; }

    public BatteryAsset? BatteryAsset { get; set; }

    public Site? Site { get; set; }

    public EnvironmentalIncident? EnvironmentalIncident { get; set; }

    public Alert? MergedIntoAlert { get; set; }

    public ICollection<Alert> MergedAlerts { get; set; } = new List<Alert>();
}
