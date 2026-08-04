using SharedContracts.Events.Root;

namespace SharedContracts.Events;

/// <summary>
/// Publish khi Ticket mới được tạo (thủ công hoặc auto từ BatteryAnomalyDetected).
/// Subscribers: NotificationService (notify Manager).
///
/// Sprint 6.2 NOTI-05 (#676) — bổ sung <c>CustomerId</c> + <c>Priority</c>: payload cũ chỉ có
/// TicketId/Code nên notification cho Manager không nói được ticket của ai, ưu tiên gì
/// (reviewnotification.md §4.1). <c>Priority</c> nullable vì ticket vừa tạo chưa qua triage
/// thì <c>Ticket.Priority</c> còn null.
/// </summary>
public record TicketCreatedEvent(
    Guid TicketId,
    string Code,
    Guid CustomerId,
    string? Priority
) : IntegrationEvent;
