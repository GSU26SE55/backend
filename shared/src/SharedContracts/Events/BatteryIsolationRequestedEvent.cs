using SharedContracts.Events.Root;

namespace SharedContracts.Events;

public record BatteryIsolationRequestedEvent(
    Guid IncidentEpisodeId,
    Guid TicketId,
    IReadOnlyCollection<Guid> BatteryAssetIds,
    DateTime RequestedAtUtc
) : IntegrationEvent;
