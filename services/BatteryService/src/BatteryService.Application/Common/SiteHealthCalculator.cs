namespace BatteryService.Application.Common;

/// <summary>
/// Công thức health score của Site — dùng chung cho endpoint per-site (<c>GET /api/sites/{id}/dashboard</c>)
/// và system-wide (<c>GET /api/sites/dashboard/stats</c>) để hai nơi không lệch số.
/// </summary>
public static class SiteHealthCalculator
{
    /// <summary>Site có health dưới ngưỡng này được coi là "cần chú ý" (at-risk) trên dashboard.</summary>
    public const int AtRiskThreshold = 80;

    /// <summary>
    /// 100 − (số asset không active × 5) − (số asset có alert đang active × 10), chặn trong [0, 100].
    /// Site chưa có asset nào → 100.
    /// </summary>
    public static int Compute(int totalAssets, int activeAssets, int assetsWithActiveAlerts)
    {
        var inactivePenalty = totalAssets == 0 ? 0 : (totalAssets - activeAssets) * 5;
        var alertPenalty = assetsWithActiveAlerts * 10;
        return Math.Clamp(100 - inactivePenalty - alertPenalty, 0, 100);
    }
}
