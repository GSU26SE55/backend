namespace BatteryService.Application.DTOs.Reports;

/// <summary>Sprint 7 #114 (§5.2) — sức khỏe pin theo loại.</summary>
public class BatteryHealthByTypeRow
{
    public string TypeId { get; set; } = string.Empty;
    public string? Name { get; set; }
    public int TotalAssets { get; set; }
    public int WithActiveAlerts { get; set; }
    public decimal HealthScore { get; set; }   // 0–100: tỷ lệ asset KHÔNG có alert active
}
