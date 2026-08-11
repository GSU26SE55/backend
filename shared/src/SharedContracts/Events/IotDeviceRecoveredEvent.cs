using SharedContracts.Events.Root;

namespace SharedContracts.Events;

/// <summary>
/// An IoT device produced enough consecutive healthy signals to close its active offline incident.
/// Producer: BatteryService. Consumer: NotificationService.
/// </summary>
public record IotDeviceRecoveredEvent(
    Guid IotDeviceId,
    string DeviceCode,
    string DisplayName,
    Guid SiteId,
    string? SiteName,
    DateTime RecoveredAt,
    DateTime? LastOfflineAt,
    Guid? AlertId,
    Guid? CustomerId = null
) : IntegrationEvent;
