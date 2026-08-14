using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Services;
using NotificationService.Domain.Entities;

namespace NotificationService.Infrastructure.Realtime;

/// <summary>
/// Sprint 6.3 NOTI3-13 (#713) — hiện thực <see cref="INotificationRealtimeNotifier"/> bằng SignalR.
///
/// **Không bao giờ ném ra ngoài:** đẩy realtime là lớp tăng tốc, không phải nguồn dữ liệu.
/// Nếu lỗi WebSocket làm hỏng luồng ghi notification thì hệ thống mất bản ghi thật để đổi lấy một
/// tiện ích hiển thị — đánh đổi sai. Client vẫn lấy đủ bằng REST polling.
/// </summary>
public class SignalRNotificationNotifier : INotificationRealtimeNotifier
{
    private readonly IHubContext<NotificationHub> _hub;
    private readonly ILogger<SignalRNotificationNotifier> _logger;

    public SignalRNotificationNotifier(
        IHubContext<NotificationHub> hub,
        ILogger<SignalRNotificationNotifier> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task NotifyCreatedAsync(Notification notification, CancellationToken ct = default)
    {
        try
        {
            await _hub.Clients
                .Group(NotificationHub.UserGroup(notification.UserId))
                .SendAsync("NotificationCreated", new
                {
                    id = notification.Id,
                    type = notification.Type,
                    channel = notification.Channel,
                    status = notification.Status,
                    title = notification.Title,
                    body = notification.Body,
                    payloadJson = notification.PayloadJson,
                    entityType = notification.EntityType,
                    entityId = notification.EntityId,
                    createdAt = notification.CreatedAt,
                }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[NotificationHub] Không đẩy được notification {NotificationId} — client sẽ nhận qua polling.",
                notification.Id);
        }
    }

    public async Task NotifyPushAsync(
        Notification notification,
        bool isCritical,
        CancellationToken ct = default)
    {
        try
        {
            await _hub.Clients
                .Group(NotificationHub.UserGroup(notification.UserId))
                .SendAsync("NotificationReceived", new
                {
                    id = notification.Id,
                    type = notification.Type,
                    channel = notification.Channel,
                    status = notification.Status,
                    title = notification.Title,
                    body = notification.Body,
                    payloadJson = notification.PayloadJson,
                    entityType = notification.EntityType,
                    entityId = notification.EntityId,
                    createdAt = notification.CreatedAt,
                    isCritical,
                }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[NotificationHub] Could not deliver local push {NotificationId}; client will catch up through REST.",
                notification.Id);
        }
    }

    public async Task NotifyUnreadCountAsync(Guid userId, int unreadCount, CancellationToken ct = default)
    {
        try
        {
            await _hub.Clients
                .Group(NotificationHub.UserGroup(userId))
                .SendAsync("UnreadCountChanged", unreadCount, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[NotificationHub] Không đẩy được badge count cho user {UserId}.", userId);
        }
    }
}
