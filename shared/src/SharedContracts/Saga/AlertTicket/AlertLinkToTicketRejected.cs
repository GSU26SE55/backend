using SharedContracts.Events.Root;

namespace SharedContracts.Saga.AlertTicket;

/// <summary>
/// BatteryService → Saga: rejection cho link Alert request.
/// Reasons: Alert không tồn tại / đã link sang Ticket khác / soft-deleted.
///
/// Saga sẽ transition sang Failed state khi nhận event này (terminal).
/// Khi đó Ticket đã tạo ở TicketService trở thành **orphan** — admin reprocess
/// hoặc reconciliation cần để cleanup (xem #239 runbook).
///
/// Sprint 5B #236 — Saga contracts.
/// </summary>
public record AlertLinkToTicketRejected(
    Guid CorrelationId,
    Guid AlertId,
    Guid TicketId,
    string Reason,
    string? ErrorCode
) : IntegrationEvent;
