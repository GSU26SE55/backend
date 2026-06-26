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
        "SOH trung bình toàn hệ (%) — latest reading mỗi asset.");

    public static readonly Gauge BelowSohThresholdCount = Metrics.CreateGauge(
        "batteries_below_soh_threshold_count",
        "Số pin có SOH dưới ngưỡng cảnh báo (mặc định 80%).");

    public static readonly Gauge AvgDcirMilliohm = Metrics.CreateGauge(
        "battery_dcir_avg_milliohm",
        "Điện trở trong (DCIR) trung bình toàn hệ (mΩ).");

    public static readonly Gauge MaxCellImbalanceMv = Metrics.CreateGauge(
        "battery_cell_imbalance_max_mv",
        "Chênh lệch điện áp cell lớn nhất toàn hệ (mV).");
}
