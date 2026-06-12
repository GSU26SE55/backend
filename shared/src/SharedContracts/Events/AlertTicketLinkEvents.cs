using SharedContracts.Events.Root;

namespace SharedContracts.Events;

/// <summary>
/// Sprint 5B B11 — TicketService publish khi đã link Alert vào Ticket
/// (Alert.TicketId được commit). Subscribers: BatteryService (audit),
/// NotificationService (gửi tới customer).
/// </summary>
public record AlertLinkedToTicketEvent(
    Guid AlertId,
    Guid TicketId,
    string TicketCode,
    bool IsReused,
    DateTime LinkedAt
) : IntegrationEvent;

/// <summary>
/// Sprint 5B B11 — TicketService publish khi từ chối link Alert → Ticket
/// (asset not found, customer mismatch, …). Subscribers: BatteryService (rollback saga),
/// NotificationService (gửi escalation lên Manager).
/// </summary>
public record AlertLinkToTicketRejectedEvent(
    Guid AlertId,
    string Reason,
    string ErrorCode,
    DateTime RejectedAt
) : IntegrationEvent;
