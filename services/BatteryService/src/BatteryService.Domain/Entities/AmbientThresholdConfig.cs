using SharedKernels.Domain;

namespace BatteryService.Domain.Entities;

/// <summary>
/// Sprint 5B #89/#92 — per-Site ngưỡng ambient temp + humidity.
/// Regular table, có audit.
/// </summary>
public class AmbientThresholdConfig : AuditableEntity
{
    public Guid SiteId { get; set; }

    public decimal? HighAmbientTempWarning { get; set; }
    public decimal? HighAmbientTempCritical { get; set; }

    public decimal? HighHumidityWarning { get; set; }
    public decimal? HighHumidityCritical { get; set; }

    /// <summary>
    /// Ngưỡng nồng độ khí gas cảnh báo & nguy hiểm (% từ 0 - 100).
    /// </summary>
    public decimal? HighGasWarning { get; set; }
    public decimal? HighGasCritical { get; set; }

    /// <summary>
    /// Combo rule: nếu temp >= threshold AND humidity >= threshold → Combo anomaly.
    /// </summary>
    public decimal? ComboTempThreshold { get; set; }
    public decimal? ComboHumidityThreshold { get; set; }

    public bool Enabled { get; set; } = true;

    public Site Site { get; set; } = null!;
}
