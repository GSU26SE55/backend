using System.Text.Json;
using MassTransit;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Domain.Enums;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace NotificationService.Application.Consumers;

/// <summary>
/// IoT device mất heartbeat → push + in-app cho operations và Customer sở hữu site.
/// Channel: Push + InApp (không email — sự kiện thường xuyên hơn ticket). Routing: §3.4 overall.md.
/// </summary>
public class IotDeviceWentOfflineConsumer : IConsumer<IotDeviceWentOfflineEvent>
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly IRecipientResolver _recipientResolver;
    private readonly ICacheService _cache;
    private readonly ILogger<IotDeviceWentOfflineConsumer> _logger;

    public IotDeviceWentOfflineConsumer(
        INotificationUnitOfWork unitOfWork,
        IRecipientResolver recipientResolver,
        ICacheService cache,
        ILogger<IotDeviceWentOfflineConsumer> logger)
    {
        _unitOfWork = unitOfWork;
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
            var incidentId = evt.AlertId ?? evt.IotDeviceId;
            await NotificationDebounce.ProcessOnceByBusinessKeyAsync(
                _cache,
                "iot-offline",
                incidentId,
                TimeSpan.FromDays(7),
                context.CancellationToken,
                async () =>
            {
                var recipientIds = (await _recipientResolver.GetActiveByRoleAsync(
                        context.CancellationToken, "Manager", "Admin", "Staff"))
                    .Where(id => id != Guid.Empty)
                    .ToList();
                if (evt.CustomerId is { } customerId && customerId != Guid.Empty)
                    recipientIds.Add(customerId);
                recipientIds = recipientIds.Distinct().ToList();
                if (recipientIds.Count == 0)
                {
                    _logger.LogWarning("No recipient resolved for IotDeviceWentOffline device={DeviceId} — skip.", evt.IotDeviceId);
                    return;
                }

                var durationMinutes = Math.Round(evt.OfflineDurationSeconds / 60.0, 1);
                var title = $"[IoT] Device offline — {evt.DeviceCode}";
                var body = $"Device \"{evt.DisplayName}\" at site \"{evt.SiteName ?? evt.SiteId.ToString()}\" lost heartbeat for {durationMinutes} minutes " +
                           $"(last seen {evt.LastSeenAt:HH:mm dd/MM/yyyy}). Affecting {evt.AffectedBatteryCount} battery asset(s).";

                var payload = JsonSerializer.Serialize(new
                {
                    iotDeviceId = evt.IotDeviceId,
                    deviceCode = evt.DeviceCode,
                    siteId = evt.SiteId,
                    // Ngày giờ ĐÃ ĐỊNH DẠNG, không phải DateTime thô: bộ render Handlebars không
                    // có helper format date, nên giá trị thô đi thẳng ra màn hình dưới dạng
                    // "2026-08-17T00:30:53.958375Z" — đúng về dữ liệu nhưng không ai đọc như vậy.
                    lastSeenAt = evt.LastSeenAt.ToString("HH:mm dd/MM/yyyy"),
                    offlineDurationSeconds = evt.OfflineDurationSeconds,
                    affectedBatteryCount = evt.AffectedBatteryCount,
                    alertId = evt.AlertId
                });

                // One SaveChanges for the entire recipient × channel fan-out. A DB failure
                // propagates so MassTransit retries; the business lease is only promoted after
                // this write succeeds.
                await NotificationWriter.WriteAsync(
                    _unitOfWork,
                    recipientIds,
                    NotificationTypeEnum.IotDeviceWentOffline,
                    NotificationWriter.InAppPush,
                    title,
                    body,
                    payload,
                    "IotDevice",
                    evt.IotDeviceId,
                    context.CancellationToken);
            });
        });
    }
}
