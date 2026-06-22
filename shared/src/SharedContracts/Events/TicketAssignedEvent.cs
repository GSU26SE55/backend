using SharedContracts.Events.Root;
namespace SharedContracts.Events;

/// <summary>
/// Publish khi Manager assign hoặc reassign Staff vào Ticket.
/// Subscribers: NotificationService (notify Staff được assign).
/// </summary>
public record TicketAssignedEvent(
    Guid TicketId,
    string Code,
    Guid StaffId,
    string Priority
) : IntegrationEvent;
