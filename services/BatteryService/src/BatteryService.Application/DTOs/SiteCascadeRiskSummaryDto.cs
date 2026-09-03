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

    // Trực quan hoá topology (không phân trang — đếm trên TOÀN BỘ asset của site, không phải
    // trang hiện tại của bảng battery list, vốn có thể chỉ hiện 10/100 asset).
    public int IndependentCount { get; set; }
    public int SeriesStringCount { get; set; }
    public int ParallelBankCount { get; set; }
    public int SeriesParallelCount { get; set; }
}
