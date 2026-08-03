using System.Text.Json;
using MassTransit;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Interfaces.Repositories;
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
public class BatteryAnomalyDetectedConsumer : IConsumer<BatteryAnomalyDetectedEvent>
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly ICacheService _cache;
    private readonly ILogger<BatteryAnomalyDetectedConsumer> _logger;

    public BatteryAnomalyDetectedConsumer(
        INotificationUnitOfWork unitOfWork,
        ICacheService cache,
        ILogger<BatteryAnomalyDetectedConsumer> logger)
    {
        _unitOfWork = unitOfWork;
        _cache = cache;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<BatteryAnomalyDetectedEvent> context)
    {
        var messageId = context.MessageId ?? Guid.Empty;
        if (messageId != Guid.Empty && !await NotificationDebounce.TryBeginByMessageAsync(_cache, messageId, context.CancellationToken))
        {
            _logger.LogInformation("Debounce: skip duplicate BatteryAnomalyDetected message={MessageId}", messageId);
            return;
        }

        var evt = context.Message;

        var recipientIds = new[] { evt.CustomerId };

        var title = $"⚠️ Bất thường pin {evt.AssetSerialNumber}";
        var body = $"Phát hiện bất thường (mức {evt.Severity}) trên pin {evt.AssetSerialNumber} lúc {evt.DetectedAt:dd/MM HH:mm}.";
        var payload = JsonSerializer.Serialize(new
        {
            alertId = evt.AlertId,
            batteryAssetId = evt.BatteryAssetId,
            customerId = evt.CustomerId,
            assetSerialNumber = evt.AssetSerialNumber,
            anomalyType = evt.AnomalyType,
            severity = evt.Severity,
            thresholdValue = evt.ThresholdValue,
            actualValue = evt.ActualValue,
            unit = evt.Unit,
            detectedAt = evt.DetectedAt,
            screen = "BatteryDetail"
        });

        await NotificationWriter.WriteAsync(
            _unitOfWork, recipientIds, NotificationTypeEnum.BatteryAnomalyDetected, NotificationWriter.AllChannels,
            title, body, payload, "Battery", evt.BatteryAssetId, context.CancellationToken);
    }
}
