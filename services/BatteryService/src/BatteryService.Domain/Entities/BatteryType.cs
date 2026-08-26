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

    /// <summary>
    /// Chu kỳ bảo trì định kỳ (tháng) cho loại pin này. <c>null</c> = dùng mặc định
    /// hệ thống (<c>PeriodicMaintenanceOptions.CycleMonths</c>).
    /// </summary>
    /// <remarks>
    /// Chu kỳ là thuộc tính của LOẠI pin, không phải hằng số toàn hệ thống: LFP chịu
    /// được 12 tháng giữa hai lần bảo trì, ắc-quy chì thì không. Trước đây chỉ có một
    /// con số duy nhất trong config nên mọi loại pin bị ép chung một chu kỳ.
    /// </remarks>
    public int? MaintenanceIntervalMonths { get; set; }

    public string? Description { get; set; }

    public ICollection<BatteryAsset> Assets { get; set; } = new List<BatteryAsset>();

    public ICollection<ThresholdConfig> ThresholdConfigs { get; set; } = new List<ThresholdConfig>();
}
