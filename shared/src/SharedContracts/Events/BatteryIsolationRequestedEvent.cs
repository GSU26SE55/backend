using SharedContracts.Events.Root;

namespace SharedContracts.Events;

/// <summary>
/// Sự cố đã được tuyên bố trên một ticket → BatteryService phải NGẮT XẢ những pin kèm theo.
/// <c>RequestedByAccountId</c> là account đã bấm Declare Incident; <c>Guid.Empty</c> = đường tự
/// động (SLA escalation), khi đó audit lệnh BMS ghi null.
/// </summary>
public record BatteryIsolationRequestedEvent(
    Guid IncidentEpisodeId,
    Guid TicketId,
    IReadOnlyCollection<Guid> BatteryAssetIds,
    DateTime RequestedAtUtc,
    Guid RequestedByAccountId = default
) : IntegrationEvent;
