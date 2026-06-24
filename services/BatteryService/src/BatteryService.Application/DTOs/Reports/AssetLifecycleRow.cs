namespace BatteryService.Application.DTOs.Reports;

/// <summary>Sprint 7 #114 (§5.2) — vòng đời asset.</summary>
public class AssetLifecycleRow
{
    public string AssetId { get; set; } = string.Empty;
    public string? SerialNumber { get; set; }
    public int AgeDays { get; set; }
    public int? CycleCount { get; set; }
    public int AlertsTotal { get; set; }
}
