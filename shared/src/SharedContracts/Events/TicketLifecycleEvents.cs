using SharedContracts.Events.Root;

namespace SharedContracts.Events;

/// <summary>
/// Sprint 6.2 NOTI-07 (#678) — nhóm event cho các state cuối vòng đời ticket.
///
/// Bối cảnh: trước sprint này TicketService chỉ publish các record cục bộ trong
/// <c>TicketService.Application.IntegrationEvents</c> (vd <c>TicketApprovedIntegrationEvent</c>).
/// MassTransit route theo full type name nên NotificationService (assembly khác) KHÔNG consume
/// được → enum <c>NotificationTypeEnum.TicketStatusChanged(3)</c> và <c>TicketClosed(5)</c> có
/// định nghĩa nhưng không producer/consumer (reviewnotification.md §4.1, §6).
///
/// Các event dưới đây nằm trong SharedContracts nên cả hai service dùng chung một contract.
/// Publish SONG SONG với event nội bộ cũ (không xoá) để không phá vỡ subscriber hiện có.
/// </summary>
public record TicketStatusChangedEvent(
    Guid TicketId,
    string Code,
    Guid CustomerId,
    Guid? StaffId,
    // OldStatus/NewStatus là giá trị TicketStatusEnum (int) — tránh reference TicketService.Domain.
    int OldStatus,
    int NewStatus,
    string OldStatusName,
    string NewStatusName
) : IntegrationEvent;

/// <summary>Manager approves completed work and closes the ticket; rating remains available during the grace period.</summary>
public record TicketApprovedEvent(
    Guid TicketId,
    string Code,
    Guid CustomerId,
    Guid ManagerId,
    string? ManagerComment,
    DateTime ApprovedAt
) : IntegrationEvent;

/// <summary>
/// Ticket bị từ chối. Hai ngữ cảnh khác nhau, phân biệt bằng <c>IsClosedRejected</c>:
/// <list type="bullet">
/// <item><c>false</c> — Manager từ chối kết quả resolve của Staff, ticket quay lại IN_PROGRESS
/// (người cần biết: Staff đang assign).</item>
/// <item><c>true</c> — Manager từ chối ngay ở bước triage vì ngoài scope, ticket vào
/// CLOSED_REJECTED (người cần biết: Customer).</item>
/// </list>
/// </summary>
public record TicketRejectedEvent(
    Guid TicketId,
    string Code,
    Guid CustomerId,
    Guid? StaffId,
    string Reason,
    bool IsClosedRejected,
    DateTime RejectedAt
) : IntegrationEvent;

/// <summary>Ticket reached the Closed lifecycle state.</summary>
public record TicketClosedEvent(
    Guid TicketId,
    string Code,
    Guid CustomerId,
    DateTime ClosedAt,
    bool IsAutoClosed,
    short? Rating
) : IntegrationEvent;

/// <summary>Customer mở lại ticket (BR-06, trong 7 ngày). Người cần biết: Manager + Staff đang assign.</summary>
public record TicketReopenedEvent(
    Guid TicketId,
    string Code,
    Guid CustomerId,
    Guid? StaffId,
    string ReopenReason,
    int ReopenCount,
    DateTime ReopenedAt,
    // Mốc Closed ngay trước lần reopen này. Consumer dùng nó để chỉ mở lại những alert đã
    // được resolve bởi đúng lần đóng ticket đó, không hồi sinh alert đã resolve từ chu kỳ cũ.
    DateTime? PreviousClosedAt = null
) : IntegrationEvent;

/// <summary>Reminds the Customer to rate an eligible Closed ticket during its grace period.</summary>
public record TicketRatingRequestedEvent(
    Guid TicketId,
    string Code,
    Guid CustomerId,
    DateTime ApprovedAt,
    int DaysPending,
    int DaysUntilRatingDeadline
) : IntegrationEvent;
