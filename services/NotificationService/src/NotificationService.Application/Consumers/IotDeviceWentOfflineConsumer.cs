using System.Text.Json;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using NotificationService.Application.CQRS.Command.Notification;
using NotificationService.Application.Services;
using NotificationService.Domain.Enums;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace NotificationService.Application.Consumers;

/// <summary>
/// Sprint IoT-1 (#249) — IoT device mất heartbeat → push + in-app cho Manager + Admin.
/// GH-604: recipient resolve qua <see cref="IRecipientResolver"/> (broadcast Manager + Admin).
/// Channel: Push + InApp (không email — sự kiện thường xuyên hơn ticket). Routing: §3.4 overall.md.
/// </summary>
public class IotDeviceWentOfflineConsumer : IConsumer<IotDeviceWentOfflineEvent>
{
    private readonly IMediator _mediator;
    private readonly IRecipientResolver _recipientResolver;
    private readonly ICacheService _cache;
    private readonly ILogger<IotDeviceWentOfflineConsumer> _logger;

    public IotDeviceWentOfflineConsumer(
        IMediator mediator,
        IRecipientResolver recipientResolver,
        ICacheService cache,
        ILogger<IotDeviceWentOfflineConsumer> logger)
    {
        _mediator = mediator;
        _recipientResolver = recipientResolver;
        _cache = cache;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<IotDeviceWentOfflineEvent> context)
    {
        // GH-765 — chỗ giữ có hạn ngắn, chỉ nâng lên cửa sổ 30 phút SAU KHI ghi xong.
        // Bản cũ chiếm key 30 phút ngay từ đầu, nên một lỗi DB/resolver ở lần đầu là mọi lần
        // gửi lại trong 30 phút đều bị coi là trùng ⇒ notification biến mất hẳn.
        await NotificationDebounce.ProcessOnceAsync(_cache, context, "IotDeviceWentOffline", _logger, async () =>
        {
            var evt = context.Message;

            var recipientIds = await _recipientResolver.GetActiveByRoleAsync(context.CancellationToken, "Manager", "Admin");
            if (recipientIds.Count == 0)
            {
                _logger.LogWarning("No Manager/Admin recipient resolved for IotDeviceWentOffline device={DeviceId} — skip.", evt.IotDeviceId);
                return;
            }

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
        });
    }
}
