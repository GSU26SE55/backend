using BatteryService.Domain.Enums;

namespace BatteryService.Application.DTOs;

public class SiteDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public Guid CustomerId { get; set; }

    public string CustomerName { get; set; } = string.Empty;

    public string? Address { get; set; }

    public decimal? Latitude { get; set; }

    public decimal? Longitude { get; set; }

    public decimal? CapacityKw { get; set; }

    public DateTime InstallDate { get; set; }

    public SiteStatusEnum Status { get; set; }

    public string? ContactPersonName { get; set; }

    public string? ContactPersonPhone { get; set; }

    public int BatteryGroupCount { get; set; }

    public int BatteryAssetCount { get; set; }

    public int ActiveBatteryAssetCount { get; set; }

    public DateTime CreatedAt { get; set; }
}
