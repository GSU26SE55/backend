using System.Text.Json;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using NotificationService.Application.CQRS.Command.Notification;
using NotificationService.Application.Templates;
using NotificationService.Domain.Enums;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace NotificationService.Application.Consumers;

/// <summary>
/// Consumer cho <see cref="BatteryAlertEscalationRequestedEvent"/>:
/// push notification + email cho Manager + Admin khi Critical Alert chưa-ack > 5 phút.
///
/// GH-593 — Notification debounce 5 phút per AlertId (overall.md §49.2): bỏ qua event trùng
/// AlertId trong 5 phút (qua <see cref="NotificationDebounce"/> + ICacheService).
///
/// Sprint 5B #238 (xem overall.md §3.4, §15 template catalog).
/// </summary>
public class BatteryAlertEscalationRequestedConsumer : IConsumer<BatteryAlertEscalationRequestedEvent>
{
    private readonly IMediator _mediator;
    private readonly ITemplateRenderer _templateRenderer;
    private readonly ICacheService _cache;
    private readonly ILogger<BatteryAlertEscalationRequestedConsumer> _logger;

    public BatteryAlertEscalationRequestedConsumer(
        IMediator mediator,
        ITemplateRenderer templateRenderer,
        ICacheService cache,
        ILogger<BatteryAlertEscalationRequestedConsumer> logger)
    {
        _mediator = mediator;
        _templateRenderer = templateRenderer;
        _cache = cache;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<BatteryAlertEscalationRequestedEvent> context)
    {
        var evt = context.Message;

        // GH-593 — debounce 5 phút per AlertId: bỏ qua event trùng trong cửa sổ.
        if (!await NotificationDebounce.TryBeginAsync(_cache, evt.AlertId, context.CancellationToken))
        {
            _logger.LogInformation(
                "Debounce: skip duplicate escalation notification AlertId={AlertId}", evt.AlertId);
            return;
        }

        // TODO #238: query Manager/Admin user IDs by CustomerId + Site permissions.
        // Placeholder: emit for AdminBroadcast user — actual recipient resolution
        // sẽ map qua AccountSyncReadModel khi Sprint 6 NotificationService finalize.
        var recipientIds = new[] { Guid.Empty };

        var title = $"[Escalation] Alert chưa ack {evt.MinutesSinceDetection} phút — {evt.AssetSerialNumber}";
        var plainBody = $"Critical anomaly detected at {evt.DetectedAt:O}. Manager attention required. " +
                        $"Value: {evt.ActualValue} {evt.Unit}.";

        var htmlBody = _templateRenderer.Render("battery-alert-escalation-pending", new
        {
            AlertId = evt.AlertId,
            AssetSerialNumber = evt.AssetSerialNumber,
            AnomalyType = evt.AnomalyType.ToString(),
            Severity = evt.Severity.ToString(),
            DetectedAt = evt.DetectedAt.ToString("dd/MM/yyyy HH:mm:ss"),
            ActualValue = evt.ActualValue,
            Unit = evt.Unit,
            MinutesSinceDetection = evt.MinutesSinceDetection
        });

        var payload = JsonSerializer.Serialize(new
        {
            alertId = evt.AlertId,
            batteryAssetId = evt.BatteryAssetId,
            severity = evt.Severity,
            anomalyType = evt.AnomalyType,
            minutesSinceDetection = evt.MinutesSinceDetection
        });

        foreach (var userId in recipientIds)
        {
            foreach (var channel in new[] { NotificationChannelEnum.Push, NotificationChannelEnum.Email, NotificationChannelEnum.InApp })
            {
                var cmd = new CreateNotificationCommand
                {
                    UserId = userId,
                    Type = NotificationTypeEnum.BatteryAlertEscalationPending,
                    Channel = channel,
                    Title = title,
                    Body = channel == NotificationChannelEnum.Email ? htmlBody : plainBody,
                    PayloadJson = payload,
                    EntityType = "Alert",
                    EntityId = evt.AlertId
                };

                var result = await _mediator.Send(cmd, context.CancellationToken);
                if (!result.IsSuccess)
                {
                    _logger.LogWarning(
                        "Failed to create escalation notification for AlertId={AlertId}: {Message}",
                        evt.AlertId, result.Message);
                }
            }
        }
    }
}
