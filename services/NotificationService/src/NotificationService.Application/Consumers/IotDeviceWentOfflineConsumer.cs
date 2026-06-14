using System.Text.Json;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using NotificationService.Application.CQRS.Command.Notification;
using NotificationService.Domain.Enums;
using SharedContracts.Events;

namespace NotificationService.Application.Consumers;

/// <summary>
/// Sprint IoT-1 (#249) — IoT device mất heartbeat → push + in-app cho Manager + Admin của site.
/// Channel: Push + InApp (không email — sự kiện thường xuyên hơn ticket).
/// Routing: §3.4 overall.md.
/// </summary>
public class IotDeviceWentOfflineConsumer : IConsumer<IotDeviceWentOfflineEvent>
{
    private readonly IMediator _mediator;
    private readonly ILogger<IotDeviceWentOfflineConsumer> _logger;

    public IotDeviceWentOfflineConsumer(IMediator mediator, ILogger<IotDeviceWentOfflineConsumer> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<IotDeviceWentOfflineEvent> context)
    {
        var evt = context.Message;

        // TODO Sprint 6 #107 — query Manager/Admin user IDs theo SiteId + AccountSyncReadModel.
        // Placeholder: AdminBroadcast.
        var recipientIds = new[] { Guid.Empty };

        var durationMinutes = Math.Round(evt.OfflineDurationSeconds / 60.0, 1);
        var title = $"[IoT] Device offline — {evt.DeviceCode}";
        var body = $"Device \"{evt.DisplayName}\" tại site \"{evt.SiteName ?? evt.SiteId.ToString()}\" mất heartbeat {durationMinutes} phút " +
                   $"(last seen {evt.LastSeenAt:O}). Ảnh hưởng {evt.AffectedBatteryCount} battery asset.";

        var payload = JsonSerializer.Serialize(new
        {
            iotDeviceId = evt.IotDeviceId,
            deviceCode = evt.DeviceCode,
            siteId = evt.SiteId,
            lastSeenAt = evt.LastSeenAt,
            offlineDurationSeconds = evt.OfflineDurationSeconds,
            affectedBatteryCount = evt.AffectedBatteryCount,
            alertId = evt.AlertId
        });

        foreach (var userId in recipientIds)
        {
            foreach (var channel in new[] { NotificationChannelEnum.Push, NotificationChannelEnum.InApp })
            {
                var cmd = new CreateNotificationCommand
                {
                    UserId = userId,
                    Type = NotificationTypeEnum.IotDeviceWentOffline,
                    Channel = channel,
                    Title = title,
                    Body = body,
                    PayloadJson = payload,
                    EntityType = "IotDevice",
                    EntityId = evt.IotDeviceId
                };
                var result = await _mediator.Send(cmd, context.CancellationToken);
                if (!result.IsSuccess)
                {
                    _logger.LogWarning(
                        "Failed to create IoT offline notification for DeviceId={DeviceId}: {Message}",
                        evt.IotDeviceId, result.Message);
                }
            }
        }
    }
}
