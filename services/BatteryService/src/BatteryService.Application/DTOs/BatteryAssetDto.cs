using BatteryService.Domain.Enums;

namespace BatteryService.Application.DTOs;

public class BatteryAssetDto
{
    public string Id { get; set; } = string.Empty;

    public string SerialNumber { get; set; } = string.Empty;

    public string BatteryTypeId { get; set; } = string.Empty;

    public string BatteryTypeName { get; set; } = string.Empty;

    public string? SiteId { get; set; }

    public string? SiteName { get; set; }

    public string? BatteryGroupId { get; set; }

    public string? BatteryGroupName { get; set; }

    public string CustomerId { get; set; } = string.Empty;

    public string CustomerName { get; set; } = string.Empty;

    public DateTime InstallDate { get; set; }

    public DateTime? WarrantyEndDate { get; set; }

    public WarrantyStatusEnum WarrantyStatus { get; set; }

    public string? Location { get; set; }

    public decimal? Latitude { get; set; }

    public decimal? Longitude { get; set; }

    public BatteryStatusEnum Status { get; set; }

    public string? Notes { get; set; }

    public DateTime? LastSensorReadingAt { get; set; }

    public DateTime CreatedAt { get; set; }
}
