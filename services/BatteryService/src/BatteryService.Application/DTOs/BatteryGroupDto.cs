namespace BatteryService.Application.DTOs;

public class BatteryGroupDto
{
    public string Id { get; set; } = string.Empty;

    public string SiteId { get; set; } = string.Empty;

    public string SiteName { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string BatteryTypeId { get; set; } = string.Empty;

    public string BatteryTypeName { get; set; } = string.Empty;

    public int BatteryCount { get; set; }

    public DateTime CreatedAt { get; set; }
}
