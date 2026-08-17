namespace BatteryService.Application.DTOs.Reports;

/// <summary>Sprint 7 #114 (§5.2) — asset sắp hết bảo hành.</summary>
public class WarrantyExpiringRow
{
    public string AssetId { get; set; } = string.Empty;
    public string? SerialNumber { get; set; }
    public DateTime? WarrantyEndDate { get; set; }
    public int? DaysRemaining { get; set; }
    public string CustomerId { get; set; } = string.Empty;
}
