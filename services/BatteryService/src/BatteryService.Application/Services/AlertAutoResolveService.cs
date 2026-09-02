using BatteryService.Application.Anomaly;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace BatteryService.Application.Services;

/// <summary>
/// Sprint 5B B10 (#158) — auto-resolve alerts Open mà không còn anomaly trong cửa sổ
/// <c>lookbackMinutes</c> gần nhất. Logic:
/// - Battery-level (BatteryAssetId != null): kiểm tra SensorReading mới nhất — nếu reading
///   gần nhất không trigger anomaly cùng loại nữa, đánh dấu Resolved.
/// - Site-level / Env (BatteryAssetId == null, SiteId != null): cùng nguyên tắc nhưng dựa
///   trên AmbientReading + AmbientThresholdConfig theo site — Env alert trước đây không có
///   đường tự resolve nào (không có Ticket vì chỉ Critical mới auto-tạo ticket), khiến Warning
///   Env luôn phải resolve tay dù cảm biến đã về ngưỡng an toàn từ lâu.
/// - Sensor mismatch / SOH degradation / device offline không auto-resolve ở đây (cần workflow riêng).
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
            var resolved = alert.BatteryAssetId is { } assetId
                ? await TryResolveBatteryAlertAsync(alert, assetId, cutoff, cancellationToken)
                : alert.SiteId is { } siteId
                    ? await TryResolveAmbientAlertAsync(alert, siteId, cutoff, cancellationToken)
                    : false;

            if (!resolved)
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

    private async Task<bool> TryResolveBatteryAlertAsync(
        Domain.Entities.Alert alert, Guid assetId, DateTime cutoff, CancellationToken cancellationToken)
    {
        // Chỉ những loại mà AnomalyRules.Detect thật sự có thể tái tạo mới được phép tự resolve.
        // Các enum lịch sử/AI/security không xuất hiện trong kết quả Detect; nếu không guard,
        // Any(...) luôn false và service sẽ resolve nhầm chúng như thể sensor đã an toàn.
        if (alert.AnomalyType is not (
            AnomalyTypeEnum.Overheat or
            AnomalyTypeEnum.Overvoltage or
            AnomalyTypeEnum.LowSoc or
            AnomalyTypeEnum.RapidDischarge or
            AnomalyTypeEnum.AbnormalCharging or
            AnomalyTypeEnum.HighInternalResistance or
            AnomalyTypeEnum.CellImbalance))
            return false;

        var asset = await _unitOfWork.BatteryAssets
            .GetAllAsync()
            .Where(b => !b.IsDeleted && b.Id == assetId)
            .Select(b => new { b.Id, b.BatteryTypeId })
            .FirstOrDefaultAsync(cancellationToken);
        if (asset is null)
            return false;

        var latest = await _unitOfWork.SensorReadings
            .GetAllAsync()
            .Where(r => r.BatteryAssetId == assetId && r.Time >= cutoff)
            .OrderByDescending(r => r.Time)
            .FirstOrDefaultAsync(cancellationToken);
        if (latest is null)
            return false;

        var threshold = await _unitOfWork.ThresholdConfigs
            .GetAllAsync()
            .Where(t => !t.IsDeleted && t.IsActive && t.BatteryTypeId == asset.BatteryTypeId)
            .OrderByDescending(t => t.EffectiveFromUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (threshold is null)
            return false;

        var stillAnomaly = AnomalyRules.Detect(latest, threshold).Any(d => d.Type == alert.AnomalyType);
        return !stillAnomaly;
    }

    private async Task<bool> TryResolveAmbientAlertAsync(
        Domain.Entities.Alert alert, Guid siteId, DateTime cutoff, CancellationToken cancellationToken)
    {
        // EnvironmentalIncident và các loại site-level khác có workflow resolve riêng. Chỉ
        // allow những loại được AnomalyRules.DetectAmbient phát hiện từ một AmbientReading.
        if (alert.AnomalyType is not (
            AnomalyTypeEnum.HighAmbientTemp or
            AnomalyTypeEnum.HighHumidity or
            AnomalyTypeEnum.HighTempHumidityCombo or
            AnomalyTypeEnum.HighGasConcentration or
            AnomalyTypeEnum.WaterLeak))
            return false;

        var latest = await _unitOfWork.AmbientReadings
            .GetAllAsync()
            .Where(r => r.SiteId == siteId && r.Time >= cutoff)
            .OrderByDescending(r => r.Time)
            .FirstOrDefaultAsync(cancellationToken);
        if (latest is null)
            return false;

        var threshold = await _unitOfWork.AmbientThresholdConfigs
            .GetAllAsync()
            .Where(t => !t.IsDeleted && t.Enabled && t.SiteId == siteId)
            .FirstOrDefaultAsync(cancellationToken);
        if (threshold is null)
            return false;

        var stillAnomaly = AnomalyRules.DetectAmbient(latest, threshold).Any(d => d.Type == alert.AnomalyType);
        return !stillAnomaly;
    }
}
