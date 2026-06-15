using BatteryService.Domain.Enums;

namespace BatteryService.Domain.Entities;

/// <summary>
/// Time-series append-only sensor sample. This intentionally does not inherit AuditableEntity.
/// </summary>
public class SensorReading
{
    public DateTime Time { get; set; }

    public Guid BatteryAssetId { get; set; }

    public decimal Voltage { get; set; }

    public decimal Current { get; set; }

    public decimal Temperature { get; set; }

    public decimal SocPercent { get; set; }

    public int? CycleCount { get; set; }

    public decimal? SohPercent { get; set; }

    public ChargingStateEnum? ChargingState { get; set; }

    public string? SourceDeviceId { get; set; }

    // Sprint 5B #101 — Tier 2 battery health metrics (nullable, backward-compat).
    public decimal? InternalResistanceMilliohm { get; set; }
    public decimal? CellVoltageDeltaMv { get; set; }

    // Sprint 5B B9 (#154) — phân biệt nguồn đo (BMS vs IoT vs External) cho cross-source validation B10.
    public SensorReadingSourceTypeEnum SourceType { get; set; } = SensorReadingSourceTypeEnum.IotGateway;

    /// <summary>BMS error raw code (vd "0x0A", "OverCurrent,CellImbalance"). Nullable.</summary>
    public string? BmsErrorCode { get; set; }

    /// <summary>
    /// §52.9 — phân biệt nhiều sensor cùng đo 1 pin (vd "primary"/"redundant"/"external-temp").
    /// 1 pin có thể có nhiều reading cùng timestamp khác <c>SensorSourceCode</c>.
    /// </summary>
    public string? SensorSourceCode { get; set; }

    public BatteryAsset BatteryAsset { get; set; } = null!;
}
