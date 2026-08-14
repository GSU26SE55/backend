using BatteryService.Domain.Entities;

namespace BatteryService.Application.Services;

public interface IIotDeviceAvailabilityService
{
    /// <summary>
    /// Records a server-accepted health signal. An Offline device needs two consecutive signals
    /// within its expected heartbeat gap before it is promoted back to Active.
    /// The caller owns SaveChanges so recovery, alert resolution, and outbox remain atomic.
    /// </summary>
    Task<IotDeviceRecoveryResult> RecordHealthySignalAsync(
        IotDevice device,
        DateTime observedAtUtc,
        bool forceActivation,
        CancellationToken cancellationToken = default);
}

public readonly record struct IotDeviceRecoveryResult(
    bool BecameActive,
    Guid? ResolvedAlertId);
