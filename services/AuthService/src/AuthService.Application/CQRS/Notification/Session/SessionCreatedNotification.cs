using MediatR;

namespace AuthService.Application.CQRS.Notification.Session;

/// <summary>
/// Notification fire-and-forget được publish ngay sau khi handler thêm 1 refresh token mới
/// vào DbContext (chưa SaveChanges).
///
/// <see cref="Handler.Session.SessionCreatedNotificationHandler"/> sẽ enforce session limit:
/// nếu account đã có >= MaxConcurrentSessions, revoke các session CŨ NHẤT theo FIFO.
///
/// Quy tắc dùng:
/// - Publish TRƯỚC SaveChangesAsync để revoke action atomic với new session.
/// - Truyền AccountId, không truyền entity (handler tự query).
/// </summary>
public sealed record SessionCreatedNotification(Guid AccountId) : INotification;
