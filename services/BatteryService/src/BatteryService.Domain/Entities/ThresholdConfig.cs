using SharedKernels.Domain;

namespace BatteryService.Domain.Entities;

public class ThresholdConfig : AuditableEntity
{
    public Guid BatteryTypeId { get; set; }

    public decimal VoltageMin { get; set; }

    public decimal VoltageMax { get; set; }

    public decimal TemperatureMax { get; set; }

    public decimal TemperatureMin { get; set; }

    public decimal SocWarningThreshold { get; set; }

    public decimal SocCriticalThreshold { get; set; }

    public decimal? CurrentMaxCharge { get; set; }

    public decimal? CurrentMaxDischarge { get; set; }

    public decimal? SohWarningThreshold { get; set; }

    public decimal? SohCriticalThreshold { get; set; }

    public DateTime EffectiveFromUtc { get; set; }

    public bool IsActive { get; set; } = true;

    public BatteryType BatteryType { get; set; } = null!;
}
