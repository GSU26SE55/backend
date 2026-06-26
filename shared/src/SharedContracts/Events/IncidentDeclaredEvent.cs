using SharedContracts.Events.Root;

namespace SharedContracts.Events;

/// <summary>
/// Publish khi Ticket bị Manager declare thành Incident (Escalated → Incident).
/// Subscribers: NotificationService (notify Admin/Manager/LeadStaff).
/// </summary>
public record IncidentDeclaredEvent(
    Guid TicketId,
    string Code,
    Guid DeclaredByUserId
) : IntegrationEvent;
