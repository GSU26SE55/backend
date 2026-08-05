using System.Text.Json;
using MassTransit;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Domain.Enums;
using SharedContracts.Events;
using SharedContracts.Interfaces;

using NotificationService.Application.Templates;

namespace NotificationService.Application.Consumers;

/// <summary>
/// Sprint 6.2 NOTI-08 (#679) — bất thường pin mức Warning/Info → notify Customer sở hữu pin.
///
/// Spec §3.4 T#12 (Warning → InApp+Push) và T#11 (Info → InApp) trước đây không đạt được vì
/// BatteryService cố ý chỉ publish event cho severity Critical để tránh spam ticket
/// (reviewnotification.md §4.3). Sprint này BatteryService publish
/// <see cref="BatteryAnomalyWarningDetectedEvent"/> — event riêng, chỉ NotificationService consume,
/// nên không sinh ticket; chống spam bằng dedup ở phía publisher.
/// </summary>
public class BatteryAnomalyWarningConsumer : IConsumer<BatteryAnomalyWarningDetectedEvent>
{
    /// <summary>Giá trị <c>AlertSeverityEnum.Info</c> bên BatteryService (enum bắt đầu từ 1).</summary>
    private const int SeverityInfo = 1;

    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly ICacheService _cache;
    private readonly ILogger<BatteryAnomalyWarningConsumer> _logger;

    public BatteryAnomalyWarningConsumer(
        INotificationUnitOfWork unitOfWork,
        ICacheService cache,
        ILogger<BatteryAnomalyWarningConsumer> logger)
    {
        _unitOfWork = unitOfWork;
        _cache = cache;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<BatteryAnomalyWarningDetectedEvent> context)
    {
        // GH-765 — chỗ giữ có hạn ngắn, chỉ nâng lên cửa sổ 30 phút SAU KHI ghi xong.
        // Bản cũ chiếm key 30 phút ngay từ đầu, nên một lỗi DB/resolver ở lần đầu là mọi lần
        // gửi lại trong 30 phút đều bị coi là trùng ⇒ notification biến mất hẳn.
        await NotificationDebounce.ProcessOnceAsync(_cache, context, "BatteryAnomalyWarning", _logger, async () =>
        {
            var evt = context.Message;

            if (evt.CustomerId == Guid.Empty)
            {
                _logger.LogWarning("BatteryAnomalyWarning alert={AlertId}: thiếu CustomerId — skip.", evt.AlertId);
                return;
            }

            var isInfo = evt.Severity == SeverityInfo;
            var serial = string.IsNullOrWhiteSpace(evt.AssetSerialNumber) ? "(không rõ serial)" : evt.AssetSerialNumber;

            // T#11 Info → chỉ InApp; T#12 Warning → InApp + Push.
            var type = isInfo ? NotificationTypeEnum.BatteryAnomalyInfo : NotificationTypeEnum.BatteryAnomalyWarning;
            var channels = isInfo ? NotificationWriter.InAppOnly : NotificationWriter.InAppPush;

            var title = isInfo
                ? $"Ghi nhận thông số pin {serial}"
                : $"⚠️ Cảnh báo pin {serial}";
            // 03/08/2026 — xem chú thích cùng chủ đề ở BatteryAnomalyDetectedConsumer.
            var anomalyLabel = BatteryAnomalyLabels.AnomalyType(evt.AnomalyTypeName, evt.AnomalyType);
            var severityLabel = BatteryAnomalyLabels.Severity(evt.SeverityName, evt.Severity);

            var body = $"{anomalyLabel} (mức {severityLabel}) trên pin {serial} " +
                       $"lúc {evt.DetectedAt:dd/MM HH:mm}" +
                       (evt.ActualValue.HasValue ? $" (giá trị {evt.ActualValue} {evt.Unit})." : ".");

            var payload = JsonSerializer.Serialize(new
            {
                alertId = evt.AlertId,
                batteryAssetId = evt.BatteryAssetId,
                customerId = evt.CustomerId,
                assetSerialNumber = evt.AssetSerialNumber,
                anomalyType = evt.AnomalyType,
                severity = evt.Severity,
                anomalyTypeName = anomalyLabel,
                severityName = severityLabel,
                thresholdValue = evt.ThresholdValue,
                actualValue = evt.ActualValue,
                unit = evt.Unit,
                detectedAt = evt.DetectedAt,
                screen = "BatteryDetail"
            });

            await NotificationWriter.WriteAsync(
                _unitOfWork, [evt.CustomerId], type, channels,
                title, body, payload, "Battery", evt.BatteryAssetId, context.CancellationToken);
        });
    }
}
