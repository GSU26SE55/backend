namespace BatteryService.Application.DTOs;

public class BatteryGroupDto
{
    public Guid Id { get; set; }

    public Guid SiteId { get; set; }

    public string SiteName { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public Guid BatteryTypeId { get; set; }

    public string BatteryTypeName { get; set; } = string.Empty;

    public int BatteryCount { get; set; }

    public DateTime CreatedAt { get; set; }
}
