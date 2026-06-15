using BatteryService.Application.Interfaces;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BatteryService.Application.Services;

/// <summary>
/// Sprint IoT-2 #IoT2-28 — implementation: tìm cặp reading lệch giữa 2 sourceType (Bms vs IotGateway).
/// Strategy: cho mỗi reading mới, lấy reading gần nhất sourceType khác cùng asset trong window 60s, so sánh.
/// </summary>
public class CrossSourceValidationService : ICrossSourceValidationService
{
    // Ngưỡng mismatch — §1.6.6 overall.md.
    private const decimal VoltageDeltaThreshold = 0.5m;
    private const decimal TemperatureDeltaThreshold = 5m;

    private static readonly TimeSpan PairingWindow = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan AlertDedupWindow = TimeSpan.FromMinutes(15);

    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IIotMetricsRecorder _metrics;
    private readonly ILogger<CrossSourceValidationService> _logger;

    public CrossSourceValidationService(
        IBatteryUnitOfWork unitOfWork,
        IIotMetricsRecorder metrics,
        ILogger<CrossSourceValidationService> logger)
    {
        _unitOfWork = unitOfWork;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<int> ScanRecentReadingsAsync(DateTime since, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var recent = await _unitOfWork.SensorReadings.GetAllAsync()
            .Where(r => r.Time >= since && r.Time <= now)
            .OrderBy(r => r.Time)
            .ToListAsync(cancellationToken);

        if (recent.Count == 0)
            return 0;

        // Group theo asset để minimize query đối chứng.
        var byAsset = recent.GroupBy(r => r.BatteryAssetId);

        var created = 0;
        foreach (var group in byAsset)
        {
            var assetId = group.Key;

            // Lấy reading cả 2 nguồn trong window quanh các reading mới.
            var minTime = group.Min(r => r.Time).Add(-PairingWindow);
            var maxTime = group.Max(r => r.Time).Add(PairingWindow);

            var window = await _unitOfWork.SensorReadings.GetAllAsync()
                .Where(r => r.BatteryAssetId == assetId
                            && r.Time >= minTime
                            && r.Time <= maxTime)
                .OrderBy(r => r.Time)
                .ToListAsync(cancellationToken);

            foreach (var reading in group)
            {
                // Tìm reading sourceType khác gần nhất trong cửa sổ.
                var counterpart = window
                    .Where(c => c.SourceType != reading.SourceType
                                && Math.Abs((c.Time - reading.Time).TotalSeconds) <= PairingWindow.TotalSeconds)
                    .OrderBy(c => Math.Abs((c.Time - reading.Time).TotalSeconds))
                    .FirstOrDefault();
                if (counterpart is null)
                    continue;

                var voltageDelta = Math.Abs(reading.Voltage - counterpart.Voltage);
                var tempDelta = Math.Abs(reading.Temperature - counterpart.Temperature);
                if (voltageDelta <= VoltageDeltaThreshold && tempDelta <= TemperatureDeltaThreshold)
                    continue;

                // Dedup theo asset + AnomalyType + cửa sổ 15 phút.
                var dedupBoundary = now.Subtract(AlertDedupWindow);
                var hasRecent = await _unitOfWork.Alerts.GetAllAsync()
                    .AnyAsync(a => !a.IsDeleted
                                   && a.BatteryAssetId == assetId
                                   && a.AnomalyType == AnomalyTypeEnum.SensorMismatch
                                   && a.DetectedAt >= dedupBoundary, cancellationToken);
                if (hasRecent)
                    continue;

                var asset = await _unitOfWork.BatteryAssets.GetAllAsync()
                    .FirstOrDefaultAsync(a => a.Id == assetId && !a.IsDeleted, cancellationToken);
                if (asset is null)
                    continue;

                await _unitOfWork.Alerts.AddAsync(new Alert
                {
                    BatteryAssetId = assetId,
                    SiteId = asset.SiteId,
                    AnomalyType = AnomalyTypeEnum.SensorMismatch,
                    Severity = AlertSeverityEnum.Warning,
                    ThresholdValue = VoltageDeltaThreshold,
                    ActualValue = voltageDelta > TemperatureDeltaThreshold ? voltageDelta : tempDelta,
                    Unit = voltageDelta > VoltageDeltaThreshold ? "V" : "°C",
                    DetectedAt = now,
                    Status = AlertStatusEnum.Open,
                    DedupWindowEndUtc = now.Add(AlertDedupWindow)
                });
                _metrics.CrossSourceMismatchAlertRecorded();
                created++;

                _logger.LogWarning(
                    "SensorMismatch on asset {AssetId}: ΔV={DeltaV:F2}V ΔT={DeltaT:F2}°C ({Src1} vs {Src2})",
                    assetId, voltageDelta, tempDelta, reading.SourceType, counterpart.SourceType);
            }
        }

        if (created > 0)
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        return created;
    }
}
