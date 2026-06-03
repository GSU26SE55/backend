using BatteryService.Domain.Enums;

namespace BatteryService.Application.DTOs;

public class BatteryTypeDto
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Manufacturer { get; set; }

    public decimal NominalCapacityAh { get; set; }

    public decimal NominalVoltage { get; set; }

    public BatteryChemistryEnum Chemistry { get; set; }

    public int MaxCycleCount { get; set; }

    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; }
}
