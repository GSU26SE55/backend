using SharedContracts.Events.Root;

namespace SharedContracts.Events;

/// <summary>An IoT device was disabled after exceeding the invalid-reading safety threshold.</summary>
public record IotDeviceAutoDecommissionedEvent(
    Guid IotDeviceId,
    string DeviceCode,
    string DisplayName,
    Guid SiteId,
    Guid AlertId,
    int RejectedReadingCount,
    DateTime WindowStartedAt,
    DateTime DecommissionedAt
) : IntegrationEvent;
