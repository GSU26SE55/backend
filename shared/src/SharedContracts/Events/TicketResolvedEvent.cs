using SharedContracts.Events.Root;
namespace SharedContracts.Events;

/// <summary>
/// Publish khi Staff resolve Ticket.
/// Subscribers: NotificationService (notify Customer + Manager).
/// </summary>
public record TicketResolvedEvent(
    Guid TicketId,
    string Code,
    Guid StaffId,
    string ResolutionSummary
) : IntegrationEvent;
