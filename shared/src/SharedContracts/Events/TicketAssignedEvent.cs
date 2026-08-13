using SharedContracts.Events.Root;
namespace SharedContracts.Events;

/// <summary>
/// Publish khi Manager assign hoặc reassign Staff vào Ticket.
/// Subscribers: NotificationService (notify Staff được assign + Customer sở hữu ticket).
///
/// Sprint 6.2 NOTI-05 (#676) — bổ sung <c>CustomerId</c> để consumer notify được Customer
/// ("Staff đang xử lý sự cố của bạn"). Trước đó consumer phải bỏ qua Customer với comment
/// "deferred (event lacks CustomerId)" (reviewnotification.md §4.1).
/// </summary>
public record TicketAssignedEvent(
    Guid TicketId,
    string Code,
    Guid PrimaryHandlerStaffId,
    string Priority,
    Guid CustomerId,
    DateTime? ScheduledStartAtUtc = null,
    int ScheduleVersion = 0,
    bool WorkStartsImmediately = false
) : IntegrationEvent;
