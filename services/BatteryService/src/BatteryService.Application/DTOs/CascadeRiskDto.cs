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

    public static CascadeRiskLevel ToLevel(decimal score) =>
        score >= 0.7m ? CascadeRiskLevel.High
        : score >= 0.5m ? CascadeRiskLevel.Medium
        : CascadeRiskLevel.Low;
}
