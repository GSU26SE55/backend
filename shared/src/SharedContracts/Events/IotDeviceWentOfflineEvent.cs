using SharedContracts.Events.Root;

namespace SharedContracts.Events;

/// <summary>
/// Sprint IoT-1 (#249) — IoT device mất heartbeat &gt; threshold (5 phút).
/// Producer: BatteryService.<c>IotDeviceOfflineDetectionBackgroundService</c>.
/// Consumer: NotificationService.<c>IotDeviceWentOfflineConsumer</c> → push + in-app alert.
/// </summary>
public record IotDeviceWentOfflineEvent(
    Guid IotDeviceId,
    string DeviceCode,
    string DisplayName,
    Guid SiteId,
    string? SiteName,
    DateTime LastSeenAt,
    DateTime DetectedAt,
    int OfflineDurationSeconds,
    /// <summary>Số BatteryAsset mà device này phục vụ (qua calibration mapping hoặc site).</summary>
    int AffectedBatteryCount,
    /// <summary>AlertId nếu backend đã tạo Alert DeviceOffline tương ứng (xem §52.15).</summary>
    Guid? AlertId
) : IntegrationEvent;
