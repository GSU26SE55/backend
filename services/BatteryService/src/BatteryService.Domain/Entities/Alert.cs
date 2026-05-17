using BatteryService.Domain.Enums;
using SharedKernels.Domain;

namespace BatteryService.Domain.Entities;

public class Alert : AuditableEntity
{
    public Guid BatteryAssetId { get; set; }

    public AnomalyTypeEnum AnomalyType { get; set; }

    public AlertSeverityEnum Severity { get; set; }

    public decimal ThresholdValue { get; set; }

    public decimal ActualValue { get; set; }

    public string Unit { get; set; } = null!;

    public DateTime DetectedAt { get; set; }

    public AlertStatusEnum Status { get; set; } = AlertStatusEnum.Open;

    public Guid? MergedIntoAlertId { get; set; }

    public Guid? TicketId { get; set; }

    public Guid? AcknowledgedByUserId { get; set; }

    public DateTime? AcknowledgedAt { get; set; }

    public DateTime? ResolvedAt { get; set; }

    public DateTime DedupWindowEndUtc { get; set; }

    public BatteryAsset BatteryAsset { get; set; } = null!;

    public Alert? MergedIntoAlert { get; set; }

    public ICollection<Alert> MergedAlerts { get; set; } = new List<Alert>();
}
