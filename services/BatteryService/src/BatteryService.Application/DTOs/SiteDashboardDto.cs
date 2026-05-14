namespace BatteryService.Application.DTOs;

public class SiteDashboardDto
{
    public Guid SiteId { get; set; }

    public string Name { get; set; } = string.Empty;

    public Guid CustomerId { get; set; }

    public int TotalAssets { get; set; }

    public int ActiveAssets { get; set; }

    public int AssetsWithActiveAlerts { get; set; }

    public decimal? TotalCapacityKw { get; set; }

    public DateTime? LastAlertAt { get; set; }

    public int HealthScore { get; set; }
}
