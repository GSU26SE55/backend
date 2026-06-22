using System.Text.Json;
using MassTransit;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Domain.Enums;
using SharedContracts.Events;

namespace NotificationService.Application.Consumers;

/// <summary>
/// GH-107 — Bất thường pin được phát hiện → notify Customer sở hữu pin (CustomerId có sẵn trong event).
/// Channel InApp + Push. (TicketService cũng consume event này để auto-tạo ticket — pub-sub độc lập.)
/// </summary>
public class BatteryAnomalyDetectedConsumer : IConsumer<BatteryAnomalyDetectedEvent>
{
    private readonly INotificationUnitOfWork _unitOfWork;

    public BatteryAnomalyDetectedConsumer(INotificationUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task Consume(ConsumeContext<BatteryAnomalyDetectedEvent> context)
    {
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
            _unitOfWork, recipientIds, NotificationTypeEnum.BatteryAnomalyDetected, NotificationWriter.InAppPush,
            title, body, payload, "Battery", evt.BatteryAssetId, context.CancellationToken);
    }
}
