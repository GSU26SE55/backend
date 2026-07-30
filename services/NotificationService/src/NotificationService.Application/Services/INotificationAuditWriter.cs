using NotificationService.Domain.Enums;

namespace NotificationService.Application.Services;

/// <summary>
/// Sprint 6.2 NOTI-13 (#684) — ghi audit forensic cho NotificationService (#AUDIT-34).
///
/// Trước sprint này hạ tầng đã dựng đủ (bảng <c>notification_audit_logs</c> 14 cột +
/// <c>notification_audit_outbox</c> + <c>NotificationAuditOutboxRelayBackgroundService</c> leader-election)
/// nhưng KHÔNG dòng code nào tạo record → enum <see cref="NotificationAuditActionEnum"/> chưa từng được
/// dùng và relay poll bảng rỗng 2 giây/lần vĩnh viễn (reviewnotification.md §4.6).
///
/// Writer này chỉ <c>AddAsync</c> vào UnitOfWork, KHÔNG <c>SaveChangesAsync</c> — caller quyết định
/// thời điểm commit để audit atomic với thay đổi nghiệp vụ. Lỗi ghi audit KHÔNG được throw ra ngoài.
/// </summary>
public interface INotificationAuditWriter
{
    /// <summary>
    /// Thêm 1 audit log + 1 entry outbox (chưa SaveChanges).
    /// </summary>
    /// <param name="action">Hành động — quyết định ActionCode/Category/Severity.</param>
    /// <param name="notificationId">Id record notification liên quan (target).</param>
    /// <param name="userId">User nhận notification (actor/target hiển thị).</param>
    /// <param name="isSuccess">Thành công hay thất bại.</param>
    /// <param name="reason">Lý do / thông điệp lỗi (đã sanitize).</param>
    /// <param name="metadata">Payload phụ (channel, type, attempt…) — serialize thành jsonb.</param>
    Task WriteAsync(
        NotificationAuditActionEnum action,
        Guid notificationId,
        Guid userId,
        bool isSuccess,
        string? reason = null,
        IReadOnlyDictionary<string, object?>? metadata = null,
        CancellationToken ct = default);
}
