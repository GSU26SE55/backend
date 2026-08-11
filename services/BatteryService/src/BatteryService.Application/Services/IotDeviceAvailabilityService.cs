using BatteryService.Application.Interfaces;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace BatteryService.Application.Services;

/// <summary>
/// Single recovery policy shared by HTTP heartbeat, accepted telemetry, MQTT online, and
/// explicit provisioning. It prevents one isolated packet from reviving a flapping device.
/// </summary>
public sealed class IotDeviceAvailabilityService : IIotDeviceAvailabilityService
{
    private static readonly TimeSpan MinimumConsecutiveSignalGap = TimeSpan.FromSeconds(90);

    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IIntegrationEventOutboxWriter _outbox;

    public IotDeviceAvailabilityService(
        IBatteryUnitOfWork unitOfWork,
        IIntegrationEventOutboxWriter outbox)
    {
        _unitOfWork = unitOfWork;
        _outbox = outbox;
    }

    public async Task<IotDeviceRecoveryResult> RecordHealthySignalAsync(
        IotDevice device,
        DateTime observedAtUtc,
        bool forceActivation,
        CancellationToken cancellationToken = default)
    {
        var observedAt = EnsureUtc(observedAtUtc);
        var previousLastSeen = device.LastSeenAt;
        var previousStatus = device.Status;

        device.LastSeenAt = observedAt;

        if (previousStatus == IotDeviceStatusEnum.Pending)
        {
            device.Status = IotDeviceStatusEnum.Active;
            return new IotDeviceRecoveryResult(false, null);
        }

        if (previousStatus != IotDeviceStatusEnum.Offline)
            return new IotDeviceRecoveryResult(false, null);

        var maxSignalGap = TimeSpan.FromSeconds(Math.Max(
            MinimumConsecutiveSignalGap.TotalSeconds,
            device.HeartbeatIntervalSeconds + 30));

        // A signal only counts as the second recovery proof when the previous accepted signal was
        // recorded after the offline transition and arrived within the device's expected cadence.
        // This deliberately rejects a lone packet after a long outage.
        var previousWasRecoverySignal = previousLastSeen.HasValue
            && observedAt - previousLastSeen.Value <= maxSignalGap
            && (!device.LastOfflineAt.HasValue || previousLastSeen.Value > device.LastOfflineAt.Value);

        if (!forceActivation && !previousWasRecoverySignal)
            return new IotDeviceRecoveryResult(false, null);

        device.Status = IotDeviceStatusEnum.Active;

        var incidents = await _unitOfWork.Alerts.GetAllAsync()
            .Where(a => !a.IsDeleted
                        && a.IotDeviceId == device.Id
                        && a.AnomalyType == AnomalyTypeEnum.DeviceOffline
                        && (a.Status == AlertStatusEnum.Open || a.Status == AlertStatusEnum.Acknowledged))
            .OrderBy(a => a.DetectedAt)
            .ToListAsync(cancellationToken);

        foreach (var incident in incidents)
        {
            incident.Status = AlertStatusEnum.Resolved;
            incident.ResolvedAt = observedAt;
            _unitOfWork.Alerts.UpdateAsync(incident);
        }

        var site = await _unitOfWork.Sites.GetAllAsync(false)
            .Where(s => s.Id == device.SiteId && !s.IsDeleted)
            .Select(s => new { s.Name, s.CustomerId })
            .FirstOrDefaultAsync(cancellationToken);

        var resolvedAlertId = incidents.FirstOrDefault()?.Id;
        await _outbox.WriteAsync(new IotDeviceRecoveredEvent(
            IotDeviceId: device.Id,
            DeviceCode: device.DeviceCode,
            DisplayName: device.DisplayName,
            SiteId: device.SiteId,
            SiteName: site?.Name,
            RecoveredAt: observedAt,
            LastOfflineAt: device.LastOfflineAt,
            AlertId: resolvedAlertId,
            CustomerId: site?.CustomerId), cancellationToken);

        return new IotDeviceRecoveryResult(true, resolvedAlertId);
    }

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        _ => value.ToUniversalTime()
    };
}
