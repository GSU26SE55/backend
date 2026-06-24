using BatteryService.Application.Interfaces;

namespace BatteryService.Infrastructure.Observability;

/// <summary>Sprint 7 #117 — bridge Application → prometheus-net cho battery health gauge.</summary>
internal sealed class BatteryHealthMetricsRecorder : IBatteryHealthMetricsRecorder
{
    public void SetHealthGauges(
        double avgSohPercent, int belowSohThresholdCount, double avgDcirMilliohm, double maxCellImbalanceMv)
    {
        BatteryHealthMetrics.AvgSohPercent.Set(avgSohPercent);
        BatteryHealthMetrics.BelowSohThresholdCount.Set(belowSohThresholdCount);
        BatteryHealthMetrics.AvgDcirMilliohm.Set(avgDcirMilliohm);
        BatteryHealthMetrics.MaxCellImbalanceMv.Set(maxCellImbalanceMv);
    }
}
