namespace BatteryService.Application.Services;

/// <summary>
/// Sprint 7 B4 (§31.7) — scan asset có Open alert, recompute CascadeRiskScore,
/// publish <c>BatteryCascadeRiskHighEvent</c> khi cross ngưỡng cao.
/// </summary>
public interface ICascadeRiskService
{
    Task<CascadeRiskScanResult> RecomputeAsync(int batchSize = 200, CancellationToken cancellationToken = default);
}
