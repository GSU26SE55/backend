using SharedContracts.Events.Root;
namespace SharedContracts.Events;

/// <summary>
/// Publish khi Staff resolve Ticket.
/// Subscribers: NotificationService (notify Customer + Manager).
///
/// Sprint 6.2 NOTI-05 (#676) — bổ sung <c>CustomerId</c>; trước đó event chỉ mang StaffId người
/// resolve nên consumer không notify được Customer (reviewnotification.md §4.1).
/// </summary>
public record TicketResolvedEvent(
    Guid TicketId,
    string Code,
    Guid StaffId,
    string ResolutionSummary,
    Guid CustomerId
) : IntegrationEvent;
