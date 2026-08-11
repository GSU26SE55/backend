using SharedContracts.Events.Root;

namespace SharedContracts.Events;

/// <summary>
/// Sprint IoT-1 (#249) — IoT device mất heartbeat &gt; threshold (5 phút).
/// Producer: BatteryService.<c>IotDeviceOfflineDetectionBackgroundService</c>.
/// Consumer: NotificationService.<c>IotDeviceWentOfflineConsumer</c> → push + in-app alert.
/// Carries the affected asset count, the canonical device-offline alert id, and the site owner id
/// so downstream routing does not need to guess tenant ownership.
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
    int AffectedBatteryCount,
    Guid? AlertId,
    Guid? CustomerId = null
) : IntegrationEvent;
