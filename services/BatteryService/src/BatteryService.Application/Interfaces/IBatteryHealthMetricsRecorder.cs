namespace BatteryService.Application.Interfaces;

/// <summary>
/// Sprint 7 #117 — set aggregate gauge sức khỏe pin (toàn hệ, KHÔNG per-asset → tránh cardinality).
/// Per-asset detail thuộc web app qua Reports API #114. Refresh bởi BatteryHealthGaugeBackgroundService.
/// </summary>
public interface IBatteryHealthMetricsRecorder
{
    void SetHealthGauges(
        double avgSohPercent,
        int belowSohThresholdCount,
        double avgDcirMilliohm,
        double maxCellImbalanceMv);
}
