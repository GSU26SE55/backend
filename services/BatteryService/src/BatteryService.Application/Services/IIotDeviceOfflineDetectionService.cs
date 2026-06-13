namespace BatteryService.Application.Services;

/// <summary>
/// Sprint IoT-1 (#248) — quét devices Active mất heartbeat > threshold,
/// chuyển sang Offline + tạo Alert + publish IotDeviceWentOfflineEvent (outbox).
/// </summary>
public interface IIotDeviceOfflineDetectionService
{
    Task<IotDeviceOfflineDetectionResult> DetectAsync(int offlineAfterSeconds, int batchSize, CancellationToken ct);
}

public record IotDeviceOfflineDetectionResult(int Scanned, int MarkedOffline);
