namespace BatteryService.Application.DTOs;

/// <summary>Sprint 7 B4 (§31.7) — heat map tổng hợp cascade risk theo site.</summary>
public class SiteCascadeRiskSummaryDto
{
    public string SiteId { get; set; } = string.Empty;
    public int TotalAssets { get; set; }
    public int HighRiskCount { get; set; }
    public int MediumRiskCount { get; set; }
    public int LowRiskCount { get; set; }
    public decimal MaxScore { get; set; }
    public List<CascadeRiskDto> HighRiskAssets { get; set; } = new();
}
