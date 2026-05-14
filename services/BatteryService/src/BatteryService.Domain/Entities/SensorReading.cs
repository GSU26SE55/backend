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

    public string? SourceDeviceId { get; set; }

    public BatteryAsset BatteryAsset { get; set; } = null!;
}
