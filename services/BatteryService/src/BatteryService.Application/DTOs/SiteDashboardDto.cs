namespace BatteryService.Application.DTOs;

public class SiteDashboardDto
{
    public string SiteId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string CustomerId { get; set; } = string.Empty;

    public int TotalAssets { get; set; }

    public int ActiveAssets { get; set; }

    public int AssetsWithActiveAlerts { get; set; }

    public decimal? TotalCapacityKw { get; set; }

    public DateTime? LastAlertAt { get; set; }

    public int HealthScore { get; set; }
}
