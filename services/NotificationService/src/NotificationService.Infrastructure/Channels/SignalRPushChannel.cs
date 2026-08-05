using NotificationService.Application.Services;
using NotificationService.Domain.Entities;
using NotificationService.Domain.Enums;

namespace NotificationService.Infrastructure.Channels;

/// <summary>
/// Self-hosted push transport. Existing Push preferences, quiet hours and rate limits still apply;
/// mobile receives the rendered payload over SignalR and creates the OS notification locally.
/// </summary>
public sealed class SignalRPushChannel : INotificationChannel
{
    private readonly INotificationRealtimeNotifier _realtime;

    public SignalRPushChannel(INotificationRealtimeNotifier realtime)
    {
        _realtime = realtime;
    }

    public NotificationChannelEnum ChannelType => NotificationChannelEnum.Push;

    public async Task<ChannelResult> SendAsync(SendRequest request, CancellationToken ct = default)
    {
        var notification = new Notification
        {
            Id = request.NotificationId,
            UserId = request.UserId,
            Type = request.Type,
            Channel = NotificationChannelEnum.Push,
            Status = NotificationStatusEnum.Sent,
            Title = request.Title,
            Body = request.Body,
            PayloadJson = request.PayloadJson,
            EntityType = request.EntityType,
            EntityId = request.EntityId,
            CreatedAt = request.CreatedAt,
        };

        await _realtime.NotifyPushAsync(notification, request.IsCritical, ct);
        return new ChannelResult(true);
    }
}
