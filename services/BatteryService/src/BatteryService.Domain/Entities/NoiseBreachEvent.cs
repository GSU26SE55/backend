using BatteryService.Domain.Enums;

namespace BatteryService.Domain.Entities;

/// <summary>
/// Sprint 5B B1 (#152) — time-series append-only ghi 1 lần threshold breach trước khi
/// frequency-based logic quyết định raise alert hay không.
///
/// Hypertable (TimescaleDB) — retention 7 ngày (background job dọn dẹp).
/// </summary>
public class NoiseBreachEvent
{
    public DateTime Time { get; set; }

    public Guid BatteryAssetId { get; set; }

    public AnomalyTypeEnum AnomalyType { get; set; }

    public decimal ThresholdValue { get; set; }
    public decimal ActualValue { get; set; }
    public string Unit { get; set; } = string.Empty;

    /// <summary>
    /// Nullable. Set khi breach này được promote thành Alert thật (đạt ngưỡng frequency).
    /// Giữ vĩnh viễn (không bị purge bởi retention) để audit.
    /// </summary>
    public Guid? PromotedToAlertId { get; set; }

    /// <summary>Sprint 5B B9 — phân biệt breach từ BMS hay IoT (cho cross-source analysis).</summary>
    public SensorReadingSourceTypeEnum SourceType { get; set; } = SensorReadingSourceTypeEnum.IotGateway;

    public BatteryAsset BatteryAsset { get; set; } = null!;
}
