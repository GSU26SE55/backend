using Microsoft.Extensions.Logging;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Domain.Enums;

namespace NotificationService.Infrastructure.Channels;

/// <summary>
/// InApp channel — notification đã có trong DB (Pending).
/// SendAsync chỉ update Status=Sent để FE polling endpoint thấy được.
/// </summary>
public class InAppChannel : INotificationChannel
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly ILogger<InAppChannel> _logger;

    public InAppChannel(INotificationUnitOfWork unitOfWork, ILogger<InAppChannel> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
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

        return new ChannelResult(true);
    }
}
