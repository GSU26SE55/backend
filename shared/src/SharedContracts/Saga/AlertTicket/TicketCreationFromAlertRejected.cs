using SharedContracts.Events.Root;

namespace SharedContracts.Saga.AlertTicket;

/// <summary>
/// TicketService → Saga: rejection cho create Ticket request.
/// Reasons: BatteryAsset không tồn tại, Customer inactive, anomaly mapping invalid, …
///
/// Saga sẽ transition sang Failed state khi nhận event này (terminal).
///
/// Sprint 5B #236 — Saga contracts.
/// </summary>
public record TicketCreationFromAlertRejected(
    Guid CorrelationId,
    Guid AlertId,
    string Reason,
    string? ErrorCode
) : IntegrationEvent;
