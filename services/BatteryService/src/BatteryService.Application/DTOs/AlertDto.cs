using BatteryService.Domain.Enums;

namespace BatteryService.Application.DTOs;

public class AlertDto
{
    public Guid Id { get; set; }

    public Guid BatteryAssetId { get; set; }

    public string BatterySerialNumber { get; set; } = string.Empty;

    public AnomalyTypeEnum AnomalyType { get; set; }

    public AlertSeverityEnum Severity { get; set; }

    public decimal ThresholdValue { get; set; }

    public decimal ActualValue { get; set; }

    public string Unit { get; set; } = string.Empty;

    public DateTime DetectedAt { get; set; }

    public AlertStatusEnum Status { get; set; }

    public Guid? TicketId { get; set; }

    public Guid? AcknowledgedByUserId { get; set; }

    public DateTime? AcknowledgedAt { get; set; }

    public DateTime? ResolvedAt { get; set; }

    public DateTime DedupWindowEndUtc { get; set; }

    public DateTime CreatedAt { get; set; }
}
