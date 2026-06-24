namespace BatteryService.Application.DTOs.Reports;

/// <summary>Sprint 7 #114 (§5.2) — sự cố môi trường.</summary>
public class EnvironmentalIncidentRow
{
    public string SiteId { get; set; } = string.Empty;
    public string IncidentType { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public DateTime DetectedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public decimal? DurationHours { get; set; }
    public bool WasFalseAlarm { get; set; }
}
