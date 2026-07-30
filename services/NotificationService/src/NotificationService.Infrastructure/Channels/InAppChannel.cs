using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Domain.Enums;

namespace NotificationService.Infrastructure.Channels;

/// <summary>
/// InApp channel — notification đã có trong DB (Pending).
/// SendAsync update Status=Sent để FE polling endpoint thấy được.
///
/// Sprint 6.3 NOTI3-13 (#713) — kèm đẩy realtime qua SignalR để client đang mở app thấy ngay,
/// không phải chờ vòng polling kế tiếp. Polling giữ nguyên làm đường dự phòng khi WebSocket rớt.
/// </summary>
public class InAppChannel : INotificationChannel
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly INotificationRealtimeNotifier? _realtime;   // Sprint 6.3 NOTI3-13 (#713)
    private readonly ILogger<InAppChannel> _logger;

    public InAppChannel(
        INotificationUnitOfWork unitOfWork,
        ILogger<InAppChannel> logger,
        // Optional để test/caller cũ không phải sửa; thiếu = chỉ mất realtime, feed vẫn đúng.
        INotificationRealtimeNotifier? realtime = null)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
        _realtime = realtime;
    }

    public NotificationChannelEnum ChannelType => NotificationChannelEnum.InApp;

    public async Task<ChannelResult> SendAsync(SendRequest request, CancellationToken ct = default)
    {
        var notification = await _unitOfWork.Notifications.GetByIdAsync(request.NotificationId);
        if (notification is null || notification.IsDeleted)
        {
            _logger.LogWarning("InAppChannel: notification {NotificationId} not found", request.NotificationId);
            return new ChannelResult(false, "Notification not found");
        }

        // Idempotent — không update lại nếu đã Sent
        if (notification.Status == NotificationStatusEnum.Sent)
            return new ChannelResult(true);

        notification.Status = NotificationStatusEnum.Sent;
        notification.SentAt = DateTime.UtcNow;
        _unitOfWork.Notifications.UpdateAsync(notification);
        await _unitOfWork.SaveChangesAsync(ct);

        // Sprint 6.3 NOTI3-13 (#713) — đẩy realtime SAU khi đã lưu, để client không bao giờ thấy
        // một thông báo mà REST chưa trả về. Notifier tự nuốt lỗi: mất realtime không được làm
        // hỏng bản ghi thật (client vẫn nhận qua polling).
        if (_realtime is not null)
        {
            await _realtime.NotifyCreatedAsync(notification, ct);

            var unread = await _unitOfWork.Notifications.GetAllAsync(false)
                .CountAsync(n => n.UserId == notification.UserId
                                 && !n.IsDeleted
                                 && n.Channel == NotificationChannelEnum.InApp
                                 && n.Status != NotificationStatusEnum.Read
                                 && n.Status != NotificationStatusEnum.Opened, ct);

            await _realtime.NotifyUnreadCountAsync(notification.UserId, unread, ct);
        }

        return new ChannelResult(true);
    }
}
