using BatteryService.Application.Interfaces;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace BatteryService.Application.Services;

/// <summary>
/// Owns the only Active -&gt; Offline transition. MQTT and the polling fallback both call this
/// service, so they share freshness checks, atomic claiming, incident deduplication, and outbox.
/// </summary>
public sealed class IotDeviceOfflineDetectionService : IIotDeviceOfflineDetectionService
{
    private const int MinimumOfflineSeconds = 15;
    private const int MaximumBatchSize = 1000;
    private const int HeartbeatJitterAllowanceSeconds = 30;

    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IIntegrationEventOutboxWriter _outbox;
    private readonly IIotMetricsRecorder _metrics;
    private readonly ILogger<IotDeviceOfflineDetectionService> _logger;

    public IotDeviceOfflineDetectionService(
        IBatteryUnitOfWork unitOfWork,
        IIntegrationEventOutboxWriter outbox,
        IIotMetricsRecorder metrics,
        ILogger<IotDeviceOfflineDetectionService> logger)
    {
        _unitOfWork = unitOfWork;
        _outbox = outbox;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<IotDeviceOfflineDetectionResult> DetectAsync(
        int offlineAfterSeconds,
        int batchSize,
        CancellationToken ct)
    {
        var requiredSilence = Math.Max(MinimumOfflineSeconds, offlineAfterSeconds);
        var boundedBatchSize = Math.Clamp(batchSize, 1, MaximumBatchSize);
        var now = DateTime.UtcNow;
        var broadThreshold = now.AddSeconds(-requiredSilence);

        // Reuse this periodic sweep for command acknowledgements as well: a
        // Pending command older than 60 seconds is terminal and must not keep
        // both mobile switches locked forever.
        var commandThreshold = now.AddSeconds(-60);
        var timedOutCommands = await _unitOfWork.IotDeviceCommands.GetAllAsync()
            .Where(command => !command.IsDeleted
                              && command.Status == IotDeviceCommandStatusEnum.Pending
                              && command.CreatedAt < commandThreshold)
            .Take(boundedBatchSize)
            .ToListAsync(ct);
        if (timedOutCommands.Count > 0)
        {
            foreach (var command in timedOutCommands)
            {
                command.Status = IotDeviceCommandStatusEnum.TimedOut;
                command.AckError = "Device did not acknowledge the command within 60 seconds.";
                command.AckedAt = now;
                _unitOfWork.IotDeviceCommands.UpdateAsync(command);
            }

            await _unitOfWork.SaveChangesAsync(ct);
            _logger.LogWarning("Marked {Count} IoT command(s) timed out", timedOutCommands.Count);
        }

        // Query broadly by the global threshold, then apply the per-device heartbeat cadence.
        // Page through the ordered set: Take() before the cadence filter can starve valid devices
        // behind long-heartbeat devices, while ToList() over the whole set can exhaust memory.
        var broadQuery = _unitOfWork.IotDevices.GetAllAsync(false)
            .Where(d => !d.IsDeleted
                        && d.Status == IotDeviceStatusEnum.Active
                        && d.LastSeenAt != null
                        && d.LastSeenAt < broadThreshold)
            .OrderBy(d => d.LastSeenAt)
            .ThenBy(d => d.Id)
            .Select(d => new OfflineCandidate(d.Id, d.LastSeenAt!.Value, d.HeartbeatIntervalSeconds));

        var candidates = new List<OfflineCandidate>(boundedBatchSize);
        var pageSize = Math.Max(100, boundedBatchSize * 4);
        var offset = 0;
        while (candidates.Count < boundedBatchSize)
        {
            var page = await broadQuery.Skip(offset).Take(pageSize).ToListAsync(ct);
            if (page.Count == 0)
                break;

            candidates.AddRange(page.Where(d =>
                (now - d.LastSeenAt).TotalSeconds >= EffectiveSilenceSeconds(
                    requiredSilence, d.HeartbeatIntervalSeconds)));
            if (page.Count < pageSize)
                break;
            offset += page.Count;
        }

        if (candidates.Count > boundedBatchSize)
            candidates.RemoveRange(boundedBatchSize, candidates.Count - boundedBatchSize);

        var markedOffline = 0;
        foreach (var candidate in candidates)
        {
            var transition = await TryMarkOfflineAsync(
                candidate.Id, now, requiredSilence, ct);
            if (transition.MarkedOffline)
                markedOffline++;
        }

        if (markedOffline > 0)
        {
            _logger.LogWarning(
                "Marked {Count} IoT device(s) offline (minimum silence {Threshold}s)",
                markedOffline,
                requiredSilence);
        }

        return new IotDeviceOfflineDetectionResult(candidates.Count, markedOffline);
    }

    public async Task<IotDeviceOfflineTransitionResult> TryMarkOfflineAsync(
        Guid deviceId,
        DateTime detectedAtUtc,
        int minimumSilenceSeconds,
        CancellationToken ct)
        => await TryMarkOfflineCoreAsync(
            deviceId,
            detectedAtUtc,
            minimumSilenceSeconds,
            enforceSilenceWindow: true,
            ct: ct);

    public async Task<IotDeviceOfflineTransitionResult> TryMarkOfflineFromLwtAsync(
        Guid deviceId,
        DateTime detectedAtUtc,
        CancellationToken ct)
        => await TryMarkOfflineCoreAsync(
            deviceId,
            detectedAtUtc,
            MinimumOfflineSeconds,
            enforceSilenceWindow: false,
            ct: ct);

    private async Task<IotDeviceOfflineTransitionResult> TryMarkOfflineCoreAsync(
        Guid deviceId,
        DateTime detectedAtUtc,
        int minimumSilenceSeconds,
        bool enforceSilenceWindow,
        CancellationToken ct)
    {
        var detectedAt = EnsureUtc(detectedAtUtc);
        var requiredSilence = Math.Max(MinimumOfflineSeconds, minimumSilenceSeconds);

        var device = await _unitOfWork.IotDevices.GetAllAsync(false)
            .Include(d => d.Site)
            .FirstOrDefaultAsync(d => d.Id == deviceId && !d.IsDeleted, ct);

        if (device is null
            || device.Status != IotDeviceStatusEnum.Active
            || !device.LastSeenAt.HasValue)
        {
            return default;
        }

        var lastSeen = EnsureUtc(device.LastSeenAt.Value);
        var effectiveSilence = EffectiveSilenceSeconds(requiredSilence, device.HeartbeatIntervalSeconds);
        var offlineDurationSeconds = Math.Max(0, (int)(detectedAt - lastSeen).TotalSeconds);
        if (enforceSilenceWindow && offlineDurationSeconds < effectiveSilence)
            return default;

        await _unitOfWork.BeginTransactionAsync();
        try
        {
            // Atomic claim: a heartbeat, MQTT LWT, another poller, or another replica can win the
            // race, but only one writer can change the exact Active/LastSeen snapshot to Offline.
            var claimed = await _unitOfWork.IotDevices.GetAllAsync()
                .Where(d => d.Id == device.Id
                            && !d.IsDeleted
                            && d.Status == IotDeviceStatusEnum.Active
                            && d.LastSeenAt == device.LastSeenAt)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(d => d.Status, IotDeviceStatusEnum.Offline)
                    .SetProperty(d => d.LastOfflineAt, detectedAt)
                    .SetProperty(d => d.UpdatedAt, detectedAt), ct);

            if (claimed == 0)
            {
                await _unitOfWork.RollbackTransactionAsync();
                return default;
            }

            var affectedAssetIds = await ResolveAffectedAssetIdsAsync(device.Id, device.SiteId, ct);

            var incident = await _unitOfWork.Alerts.GetAllAsync()
                .Where(a => !a.IsDeleted
                            && a.IotDeviceId == device.Id
                            && a.AnomalyType == AnomalyTypeEnum.DeviceOffline
                            && (a.Status == AlertStatusEnum.Open || a.Status == AlertStatusEnum.Acknowledged))
                .OrderBy(a => a.DetectedAt)
                .FirstOrDefaultAsync(ct);

            // A pre-existing unresolved incident means the business event has already been raised.
            // Refresh its measurements but do not notify again.
            var shouldQueueEvent = incident is null;
            if (incident is null)
            {
                incident = new Alert
                {
                    Id = Guid.NewGuid(),
                    IotDeviceId = device.Id,
                    SiteId = device.SiteId,
                    AnomalyType = AnomalyTypeEnum.DeviceOffline,
                    Severity = AlertSeverityEnum.Warning,
                    ThresholdValue = effectiveSilence / 60m,
                    ActualValue = offlineDurationSeconds / 60m,
                    Unit = "min",
                    DetectedAt = detectedAt,
                    Status = AlertStatusEnum.Open,
                    DedupWindowEndUtc = detectedAt.AddHours(1)
                };
                await _unitOfWork.Alerts.AddAsync(incident);
            }
            else
            {
                incident.ActualValue = offlineDurationSeconds / 60m;
                incident.DedupWindowEndUtc = detectedAt.AddHours(1);
                _unitOfWork.Alerts.UpdateAsync(incident);
            }

            if (shouldQueueEvent)
            {
                await _outbox.WriteAsync(new IotDeviceWentOfflineEvent(
                    IotDeviceId: device.Id,
                    DeviceCode: device.DeviceCode,
                    DisplayName: device.DisplayName,
                    SiteId: device.SiteId,
                    SiteName: device.Site?.Name,
                    LastSeenAt: lastSeen,
                    DetectedAt: detectedAt,
                    OfflineDurationSeconds: offlineDurationSeconds,
                    AffectedBatteryCount: affectedAssetIds.Count,
                    AlertId: incident.Id,
                    CustomerId: device.Site?.CustomerId), ct);
            }

            await _unitOfWork.CommitTransactionAsync();
            _metrics.DeviceOfflineRecorded();

            return new IotDeviceOfflineTransitionResult(true, incident.Id, shouldQueueEvent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Offline transition failed for device {DeviceId}", deviceId);
            await _unitOfWork.RollbackTransactionAsync();
            throw;
        }
    }

    private async Task<List<Guid>> ResolveAffectedAssetIdsAsync(
        Guid deviceId,
        Guid siteId,
        CancellationToken ct)
    {
        var mapped = await _unitOfWork.IotDeviceCalibrations.GetAllAsync(false)
            .Where(c => !c.IsDeleted && c.IotDeviceId == deviceId && c.BatteryAssetId != null)
            .Select(c => c.BatteryAssetId!.Value)
            .Distinct()
            .ToListAsync(ct);

        if (mapped.Count > 0)
            return mapped;

        // Legacy devices may not have calibration mappings yet. Fall back to the site instead of
        // reporting zero affected assets, but keep exactly one device-level incident.
        return await _unitOfWork.BatteryAssets.GetAllAsync(false)
            .Where(a => !a.IsDeleted && a.SiteId == siteId)
            .Select(a => a.Id)
            .ToListAsync(ct);
    }

    private static int EffectiveSilenceSeconds(int configuredMinimum, int heartbeatIntervalSeconds) =>
        Math.Max(configuredMinimum, Math.Max(MinimumOfflineSeconds, heartbeatIntervalSeconds) + HeartbeatJitterAllowanceSeconds);

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        _ => value.ToUniversalTime()
    };

    private readonly record struct OfflineCandidate(
        Guid Id,
        DateTime LastSeenAt,
        int HeartbeatIntervalSeconds);
}
