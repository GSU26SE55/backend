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
                        BatteryAssetId: alert.BatteryAssetId,
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
                    result.OutboxEventsQueued++;
                }
            }
        }

        if (result.AlertsCreated + result.AlertsMerged > 0)
            await _unitOfWork.SaveChangesAsync(cancellationToken);

        return result;
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
}
