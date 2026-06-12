using SharedContracts.Events.Root;

namespace SharedContracts.Saga.AlertTicket;

/// <summary>
/// TicketService → Saga: response thành công khi Ticket đã được create hoặc reuse cho Alert.
///
/// Reuse case: nếu đã có Ticket active cho cùng (BatteryAssetId, AnomalyCategory),
/// trả về TicketId của Ticket cũ với <c>IsReused=true</c>.
///
/// Sprint 5B #236 — Saga contracts.
/// </summary>
public record TicketCreatedFromAlertResponse(
    Guid CorrelationId,
    Guid AlertId,
    Guid TicketId,
    string TicketCode,
    bool IsReused
) : IntegrationEvent;
