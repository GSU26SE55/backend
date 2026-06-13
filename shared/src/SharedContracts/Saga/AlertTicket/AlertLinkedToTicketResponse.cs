using SharedContracts.Events.Root;

namespace SharedContracts.Saga.AlertTicket;

/// <summary>
/// BatteryService → Saga: response thành công khi Alert.TicketId đã link.
/// Saga transition sang Completed (terminal) khi nhận event này.
///
/// Sprint 5B #236 — Saga contracts.
/// </summary>
public record AlertLinkedToTicketResponse(
    Guid CorrelationId,
    Guid AlertId,
    Guid TicketId,
    DateTime LinkedAt
) : IntegrationEvent;
