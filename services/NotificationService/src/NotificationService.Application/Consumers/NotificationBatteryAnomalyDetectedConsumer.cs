using System.Linq;
using System.Text.Json;
using MassTransit;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Application.Templates;
using NotificationService.Domain.Enums;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace NotificationService.Application.Consumers;

/// <summary>
/// GH-107 — Bất thường pin CRITICAL được phát hiện → notify Customer sở hữu pin
/// (CustomerId có sẵn trong event). TicketService cũng consume event này để auto-tạo ticket —
/// pub-sub độc lập.
///
/// Sprint 6.2 NOTI-08 (#679) — bổ sung Email + SMS ngoài InApp + Push, đúng spec §3.4 T#13
/// ("Customer nhận InApp+Push+Email+SMS", SMS theo preference). Trước đó chỉ ghi InApp + Push
/// (reviewnotification.md §4.3). Việc tôn trọng preference (SmsEnabled/EmailEnabled), quiet hours
/// và thiếu email/số điện thoại đã do <c>NotificationDispatcher</c> xử lý ở tầng gửi —
/// record ghi ra ở đây chỉ là "ý định gửi".
///
/// Mức Warning/Info đi qua <see cref="BatteryAnomalyWarningConsumer"/> (event riêng, không đẻ ticket).
/// </summary>
public class NotificationBatteryAnomalyDetectedConsumer : IConsumer<BatteryAnomalyDetectedEvent>
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly ICacheService _cache;
    private readonly ILogger<NotificationBatteryAnomalyDetectedConsumer> _logger;
    private readonly IRecipientResolver _recipientResolver;

    public NotificationBatteryAnomalyDetectedConsumer(
        INotificationUnitOfWork unitOfWork,
        ICacheService cache,
        IRecipientResolver recipientResolver,
        ILogger<NotificationBatteryAnomalyDetectedConsumer> logger)
    {
        _unitOfWork = unitOfWork;
        _cache = cache;
        _recipientResolver = recipientResolver;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<BatteryAnomalyDetectedEvent> context)
    {
        // GH-765 — chỗ giữ có hạn ngắn, chỉ nâng lên cửa sổ 30 phút SAU KHI ghi xong.
        // Bản cũ chiếm key 30 phút ngay từ đầu, nên một lỗi DB/resolver ở lần đầu là mọi lần
        // gửi lại trong 30 phút đều bị coi là trùng ⇒ notification biến mất hẳn.
        await NotificationDebounce.ProcessOnceAsync(_cache, context, "BatteryAnomalyDetected", _logger, async () =>
        {
            var evt = context.Message;

            // Alert CAP SITE (nhiet do / do am / gas cua tu) di chung event nay nhung
            // `BatteryAssetId` la Guid.Empty vi su co nam o tu, khong thuoc vien pin nao.
            //
            // Truoc day danh sach nhan chi co dung khach hang: nha kho 43 do C bao cho KHACH, con
            // Manager/Admin dang truc thi khong ai duoc bao. Man "Environmental alerts" vi the
            // khong nhan duoc tin hieu realtime nao va roi han ve poll 30 giay.
            //
            // Dung dung co che cua NotificationEnvironmentalIncidentDetectedConsumer. Alert cua MOT
            // vien pin giu nguyen hanh vi cu (chi khach) — khong bien moi canh bao pin thanh thong
            // bao cho toan bo nguoi truc.
            var recipientIds = new[] { evt.CustomerId };
            if (evt.BatteryAssetId == Guid.Empty)
            {
                var operators = await _recipientResolver.GetActiveByRoleAsync(
                    context.CancellationToken, "Manager", "Admin");
                recipientIds = recipientIds.Concat(operators).Distinct().ToArray();
            }

            // 03/08/2026 — chữ đọc được thay cho số trần. Trước đó thân tin nhắn ghi "mức 3" và template
            // ghi "Loại: 4" vì hai enum này thuộc BatteryService.Domain, phía đây không tham chiếu được.
            var anomalyLabel = BatteryAnomalyLabels.AnomalyType(evt.AnomalyTypeName, evt.AnomalyType);
            var severityLabel = BatteryAnomalyLabels.Severity(evt.SeverityName, evt.Severity);

            var title = $"⚠️ Battery anomaly {evt.AssetSerialNumber}";
            var body = $"{anomalyLabel} (level {severityLabel}) on battery {evt.AssetSerialNumber} at {evt.DetectedAt:dd/MM HH:mm}.";
            var payload = JsonSerializer.Serialize(new
            {
                alertId = evt.AlertId,
                batteryAssetId = evt.BatteryAssetId,
                customerId = evt.CustomerId,
                assetSerialNumber = evt.AssetSerialNumber,
                anomalyType = evt.AnomalyType,
                severity = evt.Severity,
                // Giữ CẢ số lẫn chữ: số cho phía client lọc/so sánh, chữ cho template dựng câu.
                anomalyTypeName = anomalyLabel,
                severityName = severityLabel,
                thresholdValue = evt.ThresholdValue,
                actualValue = evt.ActualValue,
                unit = evt.Unit,
                detectedAt = evt.DetectedAt,
                screen = "BatteryDetail"
            });

            await NotificationWriter.WriteAsync(
                _unitOfWork, recipientIds, NotificationTypeEnum.BatteryAnomalyDetected, NotificationWriter.AllChannels,
                title, body, payload, "Battery", evt.BatteryAssetId, context.CancellationToken);
        });
    }
}
