using BatteryService.Domain.Enums;
using SharedKernels.Domain;

namespace BatteryService.Domain.Entities;

public class Alert : AuditableEntity
{
    /// <summary>
    /// IoT device that owns a device-level availability incident. Null for ordinary battery,
    /// environmental, and legacy alerts created before the offline-incident model was introduced.
    /// </summary>
    public Guid? IotDeviceId { get; set; }

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
    /// GH-778 — id prescription do AI cấp cho alert này, dùng để gửi phản hồi của kỹ thuật viên
    /// (accepted/edited/rejected) về AI. Null khi chưa prescribe hoặc AI không trả id.
    /// </summary>
    /// <remarks>
    /// Không lưu ở đây thì id chết ngay sau khi dựng xong đoạn text nhét vào ticket, và vòng học
    /// của AI không bao giờ khép lại — kỹ thuật viên đọc được lời khuyên nhưng không nói lại được
    /// nó đúng hay sai.
    /// </remarks>
    public string? AiPrescriptionId { get; set; }

    public BatteryAsset? BatteryAsset { get; set; }

    public IotDevice? IotDevice { get; set; }

    public Site? Site { get; set; }

    public EnvironmentalIncident? EnvironmentalIncident { get; set; }

    public Alert? MergedIntoAlert { get; set; }

    public ICollection<Alert> MergedAlerts { get; set; } = new List<Alert>();
}
