using BatteryService.Domain.Enums;

namespace BatteryService.Application.DTOs;

public class AlertDto
{
    public string Id { get; set; } = string.Empty;

    public string BatteryAssetId { get; set; } = string.Empty;

    public string BatterySerialNumber { get; set; } = string.Empty;

    public AnomalyTypeEnum AnomalyType { get; set; }

    public AlertSeverityEnum Severity { get; set; }

    public decimal? ThresholdValue { get; set; }

    public decimal? ActualValue { get; set; }

    public string? Unit { get; set; }

    public DateTime DetectedAt { get; set; }

    public AlertStatusEnum Status { get; set; }

    public string? TicketId { get; set; }

    public string? AcknowledgedByUserId { get; set; }

    public DateTime? AcknowledgedAt { get; set; }

    public DateTime? ResolvedAt { get; set; }

    public DateTime DedupWindowEndUtc { get; set; }

    public DateTime CreatedAt { get; set; }
}
