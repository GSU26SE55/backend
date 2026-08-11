using Prometheus;

namespace BatteryService.Infrastructure.Observability;

/// <summary>
/// Sprint 7 #117 — Prometheus gauge sức khỏe pin AGGREGATE (toàn hệ).
/// Dashboard Grafana "Battery Health". Cardinality thấp (không label per-asset).
/// </summary>
public static class BatteryHealthMetrics
{
    public static readonly Gauge AvgSohPercent = Metrics.CreateGauge(
        "battery_soh_avg_percent",
        "Fleet-wide average SOH (%) — latest reading per asset.");

    public static readonly Gauge BelowSohThresholdCount = Metrics.CreateGauge(
        "batteries_below_soh_threshold_count",
        "Number of batteries with SOH below the warning threshold (default 80%).");

    public static readonly Gauge AvgDcirMilliohm = Metrics.CreateGauge(
        "battery_dcir_avg_milliohm",
        "Fleet-wide average internal resistance (DCIR) in mΩ.");

    public static readonly Gauge MaxCellImbalanceMv = Metrics.CreateGauge(
        "battery_cell_imbalance_max_mv",
        "Largest fleet-wide cell voltage imbalance (mV).");
}
