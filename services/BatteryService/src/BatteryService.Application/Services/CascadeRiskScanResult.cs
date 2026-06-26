namespace BatteryService.Application.Services;

/// <summary>Sprint 7 B4 (§31.7) — kết quả 1 lần scan cascade risk.</summary>
public record CascadeRiskScanResult
{
    public int Scanned { get; set; }
    public int HighRisk { get; set; }       // số asset cross ngưỡng >= 0.7 lần này
    public int MediumRisk { get; set; }     // số asset >= 0.5 và < 0.7
}
