using BatteryService.Domain.Enums;

namespace BatteryService.Application.DTOs;

/// <summary>Sprint 7 B4 (§31.7) — cascade risk của 1 asset.</summary>
public class CascadeRiskDto
{
    public string BatteryAssetId { get; set; } = string.Empty;
    public string? SerialNumber { get; set; }
    public string? SiteId { get; set; }
    public decimal CascadeRiskScore { get; set; }
    public CascadeRiskLevel Level { get; set; }
    public ElectricalTopologyEnum ElectricalTopology { get; set; }
    public DateTime? CascadeRiskUpdatedAt { get; set; }

    /// <summary>
    /// Lý do đóng góp vào CascadeRiskScore, tính live tại thời điểm GET (không lưu DB) — chỉ để
    /// hiển thị (tooltip). Rỗng nếu không có rule nào đóng góp điểm (Low, Independent, không alert).
    /// </summary>
    public IReadOnlyList<string> RiskFactors { get; set; } = Array.Empty<string>();

    public static CascadeRiskLevel ToLevel(decimal score) =>
        score >= 0.7m ? CascadeRiskLevel.High
        : score >= 0.5m ? CascadeRiskLevel.Medium
        : CascadeRiskLevel.Low;
}
