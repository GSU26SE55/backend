using System.Text.Json;
using BatteryService.Application.Anomaly;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedContracts.Events;
using AlertEntity = BatteryService.Domain.Entities.Alert;
using OutboxEntity = BatteryService.Domain.Entities.OutboxMessage;

namespace BatteryService.Application.Services;

/// <summary>
/// Logic anomaly detection — chạy bởi ThresholdCheckBackgroundService.
/// 1) Load readings trong cửa sổ lookback
/// 2) Detect từng reading qua <see cref="AnomalyRules"/>
/// 3) BR-03 dedup merge nếu có alert "anh em" còn trong dedup window
/// 4) Ghi Outbox event cho alert Critical (atomic với SaveChanges)
/// </summary>
public class AnomalyDetectionService : IAnomalyDetectionService
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly AnomalyEngineOptions _options;

    public AnomalyDetectionService(
        IBatteryUnitOfWork unitOfWork,
        IOptions<AnomalyEngineOptions> options)
    {
        _unitOfWork = unitOfWork;
        _options = options.Value;
    }

    public async Task<AnomalyDetectionResult> ScanRecentReadingsAsync(
        TimeSpan lookbackWindow, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var cutoff = now - lookbackWindow;
        var dedupWindow = TimeSpan.FromMinutes(_options.DedupWindowMinutes);

        var readings = await _unitOfWork.SensorReadings
            .GetAllAsync()
            .Where(r => r.Time >= cutoff)
            .OrderBy(r => r.Time)
            .ToListAsync(cancellationToken);

        var result = new AnomalyDetectionResult { ReadingsScanned = readings.Count };
        if (readings.Count == 0)
            return result;

        var assetIds = readings.Select(r => r.BatteryAssetId).Distinct().ToList();
        var assets = await _unitOfWork.BatteryAssets
            .GetAllAsync()
            .Where(a => assetIds.Contains(a.Id) && !a.IsDeleted)
            .ToDictionaryAsync(a => a.Id, cancellationToken);

        var batteryTypeIds = assets.Values.Select(a => a.BatteryTypeId).Distinct().ToList();
        var thresholds = await _unitOfWork.ThresholdConfigs
            .GetAllAsync()
            .Where(t => batteryTypeIds.Contains(t.BatteryTypeId) && t.IsActive && !t.IsDeleted)
            .ToDictionaryAsync(t => t.BatteryTypeId, cancellationToken);

        foreach (var reading in readings)
        {
            if (!assets.TryGetValue(reading.BatteryAssetId, out var asset))
                continue;
            if (!thresholds.TryGetValue(asset.BatteryTypeId, out var threshold))
                continue;

            var anomalies = AnomalyRules.Detect(reading, threshold);
            if (anomalies.Count == 0)
                continue;

            foreach (var anomaly in anomalies)
            {
                // Sprint 5B B1 (#152) — Noise suppression frequency-based.
                // Bypass: EnvironmentalIncident và Critical Overheat (an toàn).
                var suppress = await ShouldSuppressByNoiseAsync(
                    reading.BatteryAssetId, anomaly, threshold, cancellationToken);
                if (suppress)
                {
                    result.AlertsSuppressed++;
                    continue;
                }

                var existing = await FindActiveAlertToMergeAsync(
                    reading.BatteryAssetId, anomaly.Type, now, cancellationToken);

                if (existing is not null)
                {
                    await _unitOfWork.Alerts.AddAsync(new AlertEntity
                    {
                        Id = Guid.NewGuid(),
                        BatteryAssetId = reading.BatteryAssetId,
                        AnomalyType = anomaly.Type,
                        Severity = anomaly.Severity,
                        ThresholdValue = anomaly.ThresholdValue,
                        ActualValue = anomaly.ActualValue,
                        Unit = anomaly.Unit,
                        DetectedAt = reading.Time,
                        Status = AlertStatusEnum.Merged,
                        MergedIntoAlertId = existing.Id,
                        DedupWindowEndUtc = existing.DedupWindowEndUtc
                    });
                    result.AlertsMerged++;
                    continue;
                }

                var alert = new AlertEntity
                {
                    Id = Guid.NewGuid(),
                    BatteryAssetId = reading.BatteryAssetId,
                    AnomalyType = anomaly.Type,
                    Severity = anomaly.Severity,
                    ThresholdValue = anomaly.ThresholdValue,
                    ActualValue = anomaly.ActualValue,
                    Unit = anomaly.Unit,
                    DetectedAt = reading.Time,
                    Status = AlertStatusEnum.Open,
                    DedupWindowEndUtc = reading.Time.Add(dedupWindow)
                };
                await _unitOfWork.Alerts.AddAsync(alert);
                result.AlertsCreated++;

                if (anomaly.Severity == AlertSeverityEnum.Critical)
                {
                    var evt = new BatteryAnomalyDetectedEvent(
                        AlertId: alert.Id,
                        BatteryAssetId: alert.BatteryAssetId ?? Guid.Empty,
                        CustomerId: asset.CustomerId,
                        AssetSerialNumber: asset.SerialNumber,
                        AnomalyType: (int)alert.AnomalyType,
                        Severity: (int)alert.Severity,
                        ThresholdValue: alert.ThresholdValue,
                        ActualValue: alert.ActualValue,
                        Unit: alert.Unit,
                        DetectedAt: alert.DetectedAt);
                    await _unitOfWork.OutboxMessages.AddAsync(new OutboxEntity
                    {
                        Id = Guid.NewGuid(),
                        AggregateId = alert.Id,
                        Type = nameof(BatteryAnomalyDetectedEvent),
                        Payload = JsonSerializer.Serialize(evt),
                        OccurredAtUtc = now
                    });

                    // Sprint IoT-2 #IoT2-30 — V2 event với SiteId + Tier-2 fields.
                    var v2 = new BatteryAnomalyDetectedV2Event(
                        AlertId: alert.Id,
                        BatteryAssetId: alert.BatteryAssetId,
                        CustomerId: asset.CustomerId,
                        SiteId: asset.SiteId,
                        AssetSerialNumber: asset.SerialNumber,
                        AnomalyType: (int)alert.AnomalyType,
                        Severity: (int)alert.Severity,
                        ThresholdValue: alert.ThresholdValue ?? 0m,
                        ActualValue: alert.ActualValue ?? 0m,
                        Unit: alert.Unit ?? string.Empty,
                        DetectedAt: alert.DetectedAt,
                        InternalResistanceMilliohm: reading.InternalResistanceMilliohm,
                        CellVoltageDeltaMv: reading.CellVoltageDeltaMv,
                        EnvironmentalIncidentId: alert.EnvironmentalIncidentId);
                    await _unitOfWork.OutboxMessages.AddAsync(new OutboxEntity
                    {
                        Id = Guid.NewGuid(),
                        AggregateId = alert.Id,
                        Type = nameof(BatteryAnomalyDetectedV2Event),
                        Payload = JsonSerializer.Serialize(v2),
                        OccurredAtUtc = now
                    });
                    result.OutboxEventsQueued++;
                }
            }
        }

        // Sprint 5B B10 (#157) — Cross-source sensor mismatch detection.
        // Group reading theo asset + cửa sổ 60s, so sánh BMS vs IoT.
        var mismatches = DetectSensorMismatches(readings, now, dedupWindow);
        foreach (var (assetId, anomaly, detectedAt) in mismatches)
        {
            if (!assets.TryGetValue(assetId, out var asset))
                continue;

            var existing = await FindActiveAlertToMergeAsync(
                assetId, AnomalyTypeEnum.SensorMismatch, now, cancellationToken);
            if (existing is not null)
            {
                await _unitOfWork.Alerts.AddAsync(new AlertEntity
                {
                    Id = Guid.NewGuid(),
                    BatteryAssetId = assetId,
                    AnomalyType = AnomalyTypeEnum.SensorMismatch,
                    Severity = anomaly.Severity,
                    ThresholdValue = anomaly.ThresholdValue,
                    ActualValue = anomaly.ActualValue,
                    Unit = anomaly.Unit,
                    DetectedAt = detectedAt,
                    Status = AlertStatusEnum.Merged,
                    MergedIntoAlertId = existing.Id,
                    DedupWindowEndUtc = existing.DedupWindowEndUtc
                });
                result.AlertsMerged++;
                continue;
            }

            await _unitOfWork.Alerts.AddAsync(new AlertEntity
            {
                Id = Guid.NewGuid(),
                BatteryAssetId = assetId,
                AnomalyType = AnomalyTypeEnum.SensorMismatch,
                Severity = anomaly.Severity,
                ThresholdValue = anomaly.ThresholdValue,
                ActualValue = anomaly.ActualValue,
                Unit = anomaly.Unit,
                DetectedAt = detectedAt,
                Status = AlertStatusEnum.Open,
                DedupWindowEndUtc = detectedAt.Add(dedupWindow)
            });
            result.AlertsCreated++;
        }

        // Sprint Bonus NS-07 (#651, N1) — AlertsSuppressed PHẢI nằm trong điều kiện save:
        // tick chỉ toàn anomaly bị nén vẫn phải persist NoiseBreachEvent pending; nếu không,
        // scope DI mới mỗi tick vứt breach event → count mãi = 0 → suppression chặn alert vĩnh viễn.
        if (result.AlertsCreated + result.AlertsMerged + result.AlertsSuppressed > 0)
            await _unitOfWork.SaveChangesAsync(cancellationToken);

        return result;
    }

    /// <summary>
    /// Sprint 5B B10 — gom reading theo asset + bucket 60s, so sánh BMS vs IoT
    /// qua <see cref="AnomalyRules.DetectSensorMismatch"/>.
    /// </summary>
    private static List<(Guid AssetId, Anomaly.AnomalyDetection Anomaly, DateTime DetectedAt)>
        DetectSensorMismatches(
            IReadOnlyList<Domain.Entities.SensorReading> readings,
            DateTime now, TimeSpan dedupWindow)
    {
        var results = new List<(Guid, Anomaly.AnomalyDetection, DateTime)>();
        var grouped = readings
            .GroupBy(r => new
            {
                r.BatteryAssetId,
                Bucket = new DateTime(r.Time.Ticks - (r.Time.Ticks % TimeSpan.TicksPerMinute), DateTimeKind.Utc)
            });

        foreach (var bucket in grouped)
        {
            var bms = bucket.FirstOrDefault(r => r.SourceType == SensorReadingSourceTypeEnum.Bms);
            var iot = bucket.FirstOrDefault(r => r.SourceType == SensorReadingSourceTypeEnum.IotGateway);
            if (bms is null || iot is null)
                continue;

            var mismatch = AnomalyRules.DetectSensorMismatch(bms, iot);
            if (mismatch is null)
                continue;

            var detectedAt = bms.Time > iot.Time ? bms.Time : iot.Time;
            results.Add((bucket.Key.BatteryAssetId, mismatch, detectedAt));
        }

        return results;
    }

    private async Task<AlertEntity?> FindActiveAlertToMergeAsync(
        Guid batteryAssetId, AnomalyTypeEnum anomalyType, DateTime now, CancellationToken ct)
    {
        return await _unitOfWork.Alerts
            .GetAllAsync()
            .Where(a => !a.IsDeleted
                        && a.BatteryAssetId == batteryAssetId
                        && a.AnomalyType == anomalyType
                        && (a.Status == AlertStatusEnum.Open || a.Status == AlertStatusEnum.Acknowledged)
                        && a.DedupWindowEndUtc > now)
            .OrderByDescending(a => a.DetectedAt)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Sprint 5B B1 (#152) — Noise suppression frequency-based.
    /// Ghi <c>NoiseBreachEvent</c> để track + chỉ raise Alert khi vi phạm
    /// >= <c>NoiseSuppressionCount</c> lần trong <c>NoiseSuppressionWindowHours</c> giờ.
    ///
    /// Bypass khi:
    /// - Threshold không bật noise suppression (return false → không suppress).
    /// - Anomaly Critical Overheat (an toàn — fire alert ngay).
    /// - Anomaly EnvironmentalIncident (site-level — bypass).
    /// </summary>
    private async Task<bool> ShouldSuppressByNoiseAsync(
        Guid batteryAssetId,
        Anomaly.AnomalyDetection anomaly,
        Domain.Entities.ThresholdConfig threshold,
        CancellationToken ct)
    {
        // Bypass — luôn fire alert ngay.
        if (anomaly.Type == AnomalyTypeEnum.EnvironmentalIncident)
            return false;
        if (anomaly.Type == AnomalyTypeEnum.Overheat && anomaly.Severity == AlertSeverityEnum.Critical)
            return false;

        // §1.3.3 — Count/Window NOT NULL với default, chỉ cần check Enabled + Count > 1.
        if (!threshold.NoiseSuppressionEnabled || threshold.NoiseSuppressionCount <= 1)
            return false;

        // Đếm breach đã có trong window TRƯỚC khi Add (row pending không được DB đếm
        // nên thứ tự này không đổi kết quả, và giữ cho phép đếm độc lập với ChangeTracker).
        var windowCutoff = DateTime.UtcNow.AddHours(-threshold.NoiseSuppressionWindowHours);
        var breachCount = await _unitOfWork.NoiseBreachEvents.GetAllAsync()
            .CountAsync(n => n.BatteryAssetId == batteryAssetId
                          && n.AnomalyType == anomaly.Type
                          && n.Time >= windowCutoff, ct);

        // Ghi breach event này.
        await _unitOfWork.NoiseBreachEvents.AddAsync(new Domain.Entities.NoiseBreachEvent
        {
            Time = DateTime.UtcNow,
            BatteryAssetId = batteryAssetId,
            AnomalyType = anomaly.Type,
            ThresholdValue = anomaly.ThresholdValue,
            ActualValue = anomaly.ActualValue,
            Unit = anomaly.Unit
        });

        // +1 vì row vừa Add chưa SaveChanges.
        return (breachCount + 1) < threshold.NoiseSuppressionCount;
    }
}
