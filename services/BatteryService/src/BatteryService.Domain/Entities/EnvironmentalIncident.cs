using BatteryService.Domain.Enums;
using SharedKernels.Domain;

namespace BatteryService.Domain.Entities;

/// <summary>
/// Sprint 5B #100 — incident environmental site-level (smoke/fire/gas/flood).
/// Regular table với lifecycle. Tách khỏi Alert (battery-level) — Alert có thể
/// reference incident qua <c>EnvironmentalIncidentId</c>.
/// </summary>
public class EnvironmentalIncident : AuditableEntity
{
    public Guid SiteId { get; set; }

    public EnvironmentalIncidentTypeEnum IncidentType { get; set; }
    public EnvironmentalIncidentStatusEnum Status { get; set; } = EnvironmentalIncidentStatusEnum.Open;

    public AlertSeverityEnum Severity { get; set; } = AlertSeverityEnum.Critical;

    public string? ReportedBy { get; set; }
    public DateTime DetectedAt { get; set; }

    public Guid? AcknowledgedBy { get; set; }
    public DateTime? AcknowledgedAt { get; set; }

    public Guid? ResolvedBy { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? ResolutionNote { get; set; }

    public Guid? FalseAlarmBy { get; set; }
    public DateTime? FalseAlarmAt { get; set; }
    public string? FalseAlarmReason { get; set; }

    public string? Notes { get; set; }

    public Site Site { get; set; } = null!;
    public ICollection<Alert> Alerts { get; set; } = new List<Alert>();
}
