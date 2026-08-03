using BatteryService.Application.Interfaces;
using BatteryService.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace BatteryService.Application.Services;

/// <summary>
/// Sprint 5B B10 (#158) — auto-resolve alerts Open mà không còn anomaly trong cửa sổ
/// <c>lookbackMinutes</c> gần nhất. Logic:
/// - Lấy batch alerts Open có DetectedAt &lt; now − lookback (đã có đủ thời gian quan sát).
/// - Nếu alert có BatteryAsset: kiểm tra reading mới nhất sau DetectedAt — nếu reading
///   gần nhất không trigger anomaly cùng loại nữa, đánh dấu Resolved.
/// - Sensor mismatch / env incident không auto-resolve ở đây (cần workflow riêng).
/// </summary>
public class AlertAutoResolveService : IAlertAutoResolveService
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public AlertAutoResolveService(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<AlertAutoResolveResult> AutoResolveAsync(
        int lookbackMinutes, int batchSize = 100, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var cutoff = now - TimeSpan.FromMinutes(lookbackMinutes);
        var result = new AlertAutoResolveResult();

        var openAlerts = await _unitOfWork.Alerts
            .GetAllAsync()
            .Where(a => !a.IsDeleted
                        && a.Status == AlertStatusEnum.Open
                        && a.BatteryAssetId != null
                        && a.AnomalyType != AnomalyTypeEnum.DeviceOffline
                        && a.AnomalyType != AnomalyTypeEnum.SensorMismatch
                        // GH-783 — alert do AI sinh: AnomalyRules.Detect() là rule ngưỡng cứng,
                        // không bao giờ trả về SohDegradation → luôn ra stillAnomaly=false →
                        // resolve nhầm, phá invariant "1 asset = 1 alert SOH chưa resolve".
                        && a.AnomalyType != AnomalyTypeEnum.SohDegradation
                        && a.DetectedAt <= cutoff)
            .OrderBy(a => a.DetectedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        result.Scanned = openAlerts.Count;
        if (openAlerts.Count == 0)
            return result;

        foreach (var alert in openAlerts)
        {
            var assetId = alert.BatteryAssetId!.Value;

            var asset = await _unitOfWork.BatteryAssets
                .GetAllAsync()
                .Where(b => !b.IsDeleted && b.Id == assetId)
                .Select(b => new { b.Id, b.BatteryTypeId })
                .FirstOrDefaultAsync(cancellationToken);
            if (asset is null)
                continue;

            var latest = await _unitOfWork.SensorReadings
                .GetAllAsync()
                .Where(r => r.BatteryAssetId == assetId && r.Time >= cutoff)
                .OrderByDescending(r => r.Time)
                .FirstOrDefaultAsync(cancellationToken);

            if (latest is null)
                continue;

            var threshold = await _unitOfWork.ThresholdConfigs
                .GetAllAsync()
                .Where(t => !t.IsDeleted && t.IsActive && t.BatteryTypeId == asset.BatteryTypeId)
                .OrderByDescending(t => t.EffectiveFromUtc)
                .FirstOrDefaultAsync(cancellationToken);

            if (threshold is null)
                continue;

            var stillAnomaly = Anomaly.AnomalyRules.Detect(latest, threshold)
                .Any(d => d.Type == alert.AnomalyType);

            if (stillAnomaly)
                continue;

            alert.Status = AlertStatusEnum.Resolved;
            alert.ResolvedAt = now;
            _unitOfWork.Alerts.UpdateAsync(alert);
            result.Resolved++;
        }

        if (result.Resolved > 0)
            await _unitOfWork.SaveChangesAsync(cancellationToken);

        return result;
    }
}
