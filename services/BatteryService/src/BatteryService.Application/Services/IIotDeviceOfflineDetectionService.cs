namespace BatteryService.Application.Services;

/// <summary>
/// Sprint IoT-1 (#248) — quét devices Active mất heartbeat > threshold,
/// chuyển sang Offline + tạo Alert + publish IotDeviceWentOfflineEvent (outbox).
/// </summary>
public interface IIotDeviceOfflineDetectionService
{
    Task<IotDeviceOfflineDetectionResult> DetectAsync(int offlineAfterSeconds, int batchSize, CancellationToken ct);

    Task<IotDeviceOfflineTransitionResult> TryMarkOfflineAsync(
        Guid deviceId,
        DateTime detectedAtUtc,
        int minimumSilenceSeconds,
        CancellationToken ct);
}

public record IotDeviceOfflineDetectionResult(int Scanned, int MarkedOffline);

public readonly record struct IotDeviceOfflineTransitionResult(
    bool MarkedOffline,
    Guid? AlertId,
    bool EventQueued);
