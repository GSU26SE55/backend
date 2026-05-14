using BatteryService.Domain.Enums;

namespace BatteryService.Application.DTOs;

public class BatteryAssetDto
{
    public Guid Id { get; set; }

    public string SerialNumber { get; set; } = string.Empty;

    public Guid BatteryTypeId { get; set; }

    public string BatteryTypeName { get; set; } = string.Empty;

    public Guid? SiteId { get; set; }

    public string? SiteName { get; set; }

    public Guid? BatteryGroupId { get; set; }

    public string? BatteryGroupName { get; set; }

    public Guid CustomerId { get; set; }

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
