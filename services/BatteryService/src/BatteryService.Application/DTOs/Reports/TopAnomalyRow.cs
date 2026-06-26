namespace BatteryService.Application.DTOs.Reports;

/// <summary>Sprint 7 #114 (§5.2) — top loại anomaly.</summary>
public class TopAnomalyRow
{
    public string AnomalyType { get; set; } = string.Empty;
    public int Count { get; set; }
    public int CriticalCount { get; set; }
}
