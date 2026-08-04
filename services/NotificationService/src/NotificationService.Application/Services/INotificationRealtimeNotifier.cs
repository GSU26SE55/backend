using NotificationService.Domain.Entities;

namespace NotificationService.Application.Services;

/// <summary>
/// Sprint 6.3 NOTI3-13 (#713) — đẩy notification mới xuống client đang mở app.
///
/// **Vấn đề:** feed in-app chỉ cập nhật khi client tự gọi lại API. Người dùng đang mở màn hình
/// thông báo phải kéo xuống làm mới mới thấy cảnh báo vừa xảy ra — trong khi đây chính là hệ thống
/// giám sát pin, nơi độ trễ vài chục giây là có ý nghĩa.
///
/// Abstraction đặt ở Application để tầng này KHÔNG phải tham chiếu <c>Microsoft.AspNetCore.SignalR</c>
/// (đúng khuôn <c>ITicketChatRealtimeNotifier</c> bên TicketService). Hiện thực là
/// <c>SignalRNotificationNotifier</c>; test và môi trường không có realtime dùng
/// <see cref="NullNotificationRealtimeNotifier"/>.
///
/// **Polling vẫn giữ nguyên:** realtime là lớp tăng tốc, không phải nguồn dữ liệu duy nhất —
/// WebSocket rớt thì client vẫn phải lấy được đủ thông báo bằng REST.
/// </summary>
public interface INotificationRealtimeNotifier
{
    /// <summary>Đẩy một notification mới tới đúng người nhận (mọi thiết bị đang kết nối).</summary>
    Task NotifyCreatedAsync(Notification notification, CancellationToken ct = default);

    /// <summary>Đẩy số chưa đọc mới để client cập nhật badge mà không phải gọi lại API.</summary>
    Task NotifyUnreadCountAsync(Guid userId, int unreadCount, CancellationToken ct = default);
}

/// <summary>Không làm gì — dùng cho unit test và môi trường chưa bật realtime.</summary>
public sealed class NullNotificationRealtimeNotifier : INotificationRealtimeNotifier
{
    public Task NotifyCreatedAsync(Notification notification, CancellationToken ct = default) => Task.CompletedTask;

    public Task NotifyUnreadCountAsync(Guid userId, int unreadCount, CancellationToken ct = default) => Task.CompletedTask;
}
