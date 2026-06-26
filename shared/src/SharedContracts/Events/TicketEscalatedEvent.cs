using SharedContracts.Events.Root;

namespace SharedContracts.Events;

/// <summary>
/// Publish khi Ticket bị escalate (SLA breach hoặc Staff/Manager request).
/// Reason là int (serialize từ EscalationReasonEnum) để tránh reference TicketService.Domain.
/// Subscribers: NotificationService (notify Manager + Admin).
/// </summary>
public record TicketEscalatedEvent(
    Guid TicketId,
    string Code,
    int Reason,
    string? Note,
    Guid? StaffId,
    string? StaffName
) : IntegrationEvent;
