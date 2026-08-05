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
/// Sprint Bonus NS-14 (#658, R3) — consume <see cref="BatteryCascadeRiskHighEvent"/> → notify
/// Manager (+ Admin): cascade risk cao = nguy cơ lan truyền cần con người điều phối (điều Staff tier
/// cao, cô lập pin). Trước fix, doc nói NotificationService notify Manager nhưng KHÔNG có consumer nào
/// → kênh duy nhất Manager biết là tự mở heat map poll.
///
/// <para>Push + email (cảnh báo an toàn) — dùng <see cref="NotificationWriter.InAppPushEmail"/>.
/// Recipient resolve theo role (broadcast Manager + Admin) như <c>SlaBreachedConsumer</c>; site-scoping
/// theo Manager của site để lại sau (resolver hiện chỉ theo role — issue #604).</para>
/// </summary>
public class BatteryCascadeRiskHighConsumer : IConsumer<BatteryCascadeRiskHighEvent>
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly IRecipientResolver _recipientResolver;
    private readonly ICacheService _cache;
    private readonly ILogger<BatteryCascadeRiskHighConsumer> _logger;

    public BatteryCascadeRiskHighConsumer(
        INotificationUnitOfWork unitOfWork,
        IRecipientResolver recipientResolver,
        ICacheService cache,
        ILogger<BatteryCascadeRiskHighConsumer> logger)
    {
        _unitOfWork = unitOfWork;
        _recipientResolver = recipientResolver;
        _cache = cache;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<BatteryCascadeRiskHighEvent> context)
    {
        // GH-765 — chỗ giữ có hạn ngắn, chỉ nâng lên cửa sổ 30 phút SAU KHI ghi xong.
        // Bản cũ chiếm key 30 phút ngay từ đầu, nên một lỗi DB/resolver ở lần đầu là mọi lần
        // gửi lại trong 30 phút đều bị coi là trùng ⇒ notification biến mất hẳn.
        await NotificationDebounce.ProcessOnceAsync(_cache, context, "CascadeRiskHigh", _logger, async () =>
        {
            var evt = context.Message;

            var recipientIds = await _recipientResolver.GetActiveByRoleAsync(context.CancellationToken, "Manager", "Admin");
            if (recipientIds.Count == 0)
            {
                _logger.LogWarning("No Manager/Admin recipient resolved for CascadeRiskHigh asset={AssetId} — skip.", evt.BatteryAssetId);
                return;
            }

            var title = "🔴 Nguy cơ lan truyền (cascade risk) cao";
            var body = $"Pin {evt.AssetSerialNumber} có cascade risk {evt.CascadeRiskScore:P0} (≥ 70%). "
                     + "Cần điều Staff kiểm tra/cô lập ngay để tránh lan sang pin lân cận.";
            var payload = JsonSerializer.Serialize(new
            {
                batteryAssetId = evt.BatteryAssetId,
                siteId = evt.SiteId,
                cascadeRiskScore = evt.CascadeRiskScore,
                relatedTicketId = evt.RelatedTicketId,
                screen = "BatteryDetail"
            });

            await NotificationWriter.WriteAsync(
                _unitOfWork, recipientIds, NotificationTypeEnum.CascadeRiskHigh, NotificationWriter.InAppPushEmail,
                title, body, payload, "BatteryAsset", evt.BatteryAssetId, context.CancellationToken);
        });
    }
}
