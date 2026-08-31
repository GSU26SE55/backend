using SharedKernels.Domain;

namespace BatteryService.Domain.Entities;

public class ThresholdConfig : AuditableEntity
{
    public Guid BatteryTypeId { get; set; }

    /// <summary>V — dưới mốc này là Warning. Mốc Critical là <see cref="VoltageMinCritical"/>.</summary>
    public decimal VoltageMin { get; set; }

    /// <summary>V — vượt mốc này là Warning. Mốc Critical là <see cref="VoltageMaxCritical"/>.</summary>
    public decimal VoltageMax { get; set; }

    /// <summary>°C — vượt mốc này là Warning. Mốc Critical là <see cref="TemperatureMaxCritical"/>.</summary>
    public decimal TemperatureMax { get; set; }

    /// <summary>°C — dưới mốc này là Warning. Mốc Critical là <see cref="TemperatureMinCritical"/>.</summary>
    public decimal TemperatureMin { get; set; }

    public decimal SocWarningThreshold { get; set; }

    public decimal SocCriticalThreshold { get; set; }

    public decimal? CurrentMaxCharge { get; set; }

    public decimal? CurrentMaxDischarge { get; set; }

    public decimal? SohWarningThreshold { get; set; }

    public decimal? SohCriticalThreshold { get; set; }

    // Sprint 5B #101 — Tier 2 thresholds (nullable, per-batttery-type).
    public decimal? InternalResistanceMaxMilliohm { get; set; }
    public decimal? CellVoltageDeltaMaxMv { get; set; }

    // Sprint 5B B1 (#152) — Noise suppression: chỉ alert nếu vi phạm
    // >= NoiseSuppressionCount lần trong WindowHours giờ liên tiếp.
    public int NoiseSuppressionCount { get; set; } = 5;
    public int NoiseSuppressionWindowHours { get; set; } = 24;
    public bool NoiseSuppressionEnabled { get; set; } = true;

    public DateTime EffectiveFromUtc { get; set; }

    public bool IsActive { get; set; } = true;

    public BatteryType BatteryType { get; set; } = null!;
}
