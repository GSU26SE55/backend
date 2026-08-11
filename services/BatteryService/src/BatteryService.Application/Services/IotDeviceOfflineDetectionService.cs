using BatteryService.Application.Interfaces;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace BatteryService.Application.Services;

public class IotDeviceOfflineDetectionService : IIotDeviceOfflineDetectionService
{
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

    public async Task<IotDeviceOfflineDetectionResult> DetectAsync(int offlineAfterSeconds, int batchSize, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var threshold = now.AddSeconds(-offlineAfterSeconds);

        // Reuse this periodic sweep for command acknowledgements as well: a
        // Pending command older than 60 seconds is terminal and must not keep
        // both mobile switches locked forever.
        var commandThreshold = now.AddSeconds(-60);
        var timedOutCommands = await _unitOfWork.IotDeviceCommands.GetAllAsync()
            .Where(command => !command.IsDeleted
                              && command.Status == IotDeviceCommandStatusEnum.Pending
                              && command.CreatedAt < commandThreshold)
            .Take(batchSize)
            .ToListAsync(ct);
        if (timedOutCommands.Count > 0)
        {
            foreach (var command in timedOutCommands)
            {
                command.Status = IotDeviceCommandStatusEnum.TimedOut;
                command.AckError = "Thiết bị không phản hồi lệnh trong 60 giây.";
                command.AckedAt = now;
                _unitOfWork.IotDeviceCommands.UpdateAsync(command);
            }

            await _unitOfWork.SaveChangesAsync(ct);
            _logger.LogWarning("Marked {Count} IoT command(s) timed out", timedOutCommands.Count);
        }

        var candidates = await _unitOfWork.IotDevices.GetAllAsync()
            .Include(d => d.Site)
            .Where(d => !d.IsDeleted
                        && d.Status == IotDeviceStatusEnum.Active
                        && d.LastSeenAt != null
                        && d.LastSeenAt < threshold)
            .Take(batchSize)
            .ToListAsync(ct);

        if (candidates.Count == 0)
            return new IotDeviceOfflineDetectionResult(0, 0);

        var markedOffline = 0;
        await _unitOfWork.BeginTransactionAsync();
        try
        {
            foreach (var device in candidates)
            {
                var lastSeen = device.LastSeenAt ?? now;
                var offlineDuration = (int)(now - lastSeen).TotalSeconds;

                device.Status = IotDeviceStatusEnum.Offline;
                device.LastOfflineAt = now;
                _unitOfWork.IotDevices.UpdateAsync(device);

                Guid? alertId = null;
                var affectedAssetIds = await _unitOfWork.BatteryAssets.GetAllAsync()
                    .Where(a => !a.IsDeleted && a.SiteId == device.SiteId)
                    .Select(a => a.Id)
                    .ToListAsync(ct);

                if (affectedAssetIds.Count > 0)
                {
                    var primaryAssetId = affectedAssetIds.First();
                    var alert = new Alert
                    {
                        Id = Guid.NewGuid(),
                        BatteryAssetId = primaryAssetId,
                        SiteId = device.SiteId,
                        AnomalyType = AnomalyTypeEnum.DeviceOffline,
                        Severity = AlertSeverityEnum.Warning,
                        DetectedAt = now,
                        Status = AlertStatusEnum.Open,
                        DedupWindowEndUtc = now.AddMinutes(5),
                        Unit = "min"
                    };
                    await _unitOfWork.Alerts.AddAsync(alert);
                    alertId = alert.Id;
                }

                await _outbox.WriteAsync(new IotDeviceWentOfflineEvent(
                    IotDeviceId: device.Id,
                    DeviceCode: device.DeviceCode,
                    DisplayName: device.DisplayName,
                    SiteId: device.SiteId,
                    SiteName: device.Site?.Name,
                    LastSeenAt: lastSeen,
                    DetectedAt: now,
                    OfflineDurationSeconds: offlineDuration,
                    AffectedBatteryCount: affectedAssetIds.Count,
                    AlertId: alertId
                ), ct);

                markedOffline++;
                _metrics.DeviceOfflineRecorded();
            }
            await _unitOfWork.CommitTransactionAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Offline detection failed");
            await _unitOfWork.RollbackTransactionAsync();
            throw;
        }

        _logger.LogWarning("Marked {Count} IoT device(s) offline (threshold {Threshold}s)", markedOffline, offlineAfterSeconds);
        return new IotDeviceOfflineDetectionResult(candidates.Count, markedOffline);
    }
}
