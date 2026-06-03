using BatteryService.Domain.Enums;
using SharedKernels.Domain;

namespace BatteryService.Domain.Entities;

public class BatteryType : AuditableEntity
{
    public string Name { get; set; } = null!;

    public string? Manufacturer { get; set; }

    public decimal NominalCapacityAh { get; set; }

    public decimal NominalVoltage { get; set; }

    public BatteryChemistryEnum Chemistry { get; set; } = BatteryChemistryEnum.LiFePO4;

    public int MaxCycleCount { get; set; } = 2000;

    public string? Description { get; set; }

    public ICollection<BatteryAsset> Assets { get; set; } = new List<BatteryAsset>();

    public ICollection<ThresholdConfig> ThresholdConfigs { get; set; } = new List<ThresholdConfig>();
}
