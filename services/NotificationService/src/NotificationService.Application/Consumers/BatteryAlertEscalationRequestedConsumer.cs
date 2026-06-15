using System.Text.Json;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using NotificationService.Application.CQRS.Command.Notification;
using NotificationService.Domain.Enums;
using SharedContracts.Events;

namespace NotificationService.Application.Consumers;

/// <summary>
/// Consumer cho <see cref="BatteryAlertEscalationRequestedEvent"/>:
/// push notification + email cho Manager + Admin khi Critical Alert chưa-ack > 5 phút.
///
/// Notification debounce 5 phút per AlertId (xem overall.md §49.2) — handled
/// bởi NotificationDispatcher trước khi send.
///
/// Sprint 5B #238 (xem overall.md §3.4, §15 template catalog).
/// </summary>
public class BatteryAlertEscalationRequestedConsumer : IConsumer<BatteryAlertEscalationRequestedEvent>
{
    private readonly IMediator _mediator;
    private readonly ILogger<BatteryAlertEscalationRequestedConsumer> _logger;

    public BatteryAlertEscalationRequestedConsumer(
        IMediator mediator,
        ILogger<BatteryAlertEscalationRequestedConsumer> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<BatteryAlertEscalationRequestedEvent> context)
    {
        var evt = context.Message;

        // TODO #238: query Manager/Admin user IDs by CustomerId + Site permissions.
        // Placeholder: emit for AdminBroadcast user — actual recipient resolution
        // sẽ map qua AccountSyncReadModel khi Sprint 6 NotificationService finalize.
        var recipientIds = new[] { Guid.Empty };

        var title = $"[Escalation] Alert chưa ack {evt.MinutesSinceDetection} phút — {evt.AssetSerialNumber}";
        var body = $"Critical anomaly detected at {evt.DetectedAt:O}. Manager attention required. " +
                   $"Value: {evt.ActualValue} {evt.Unit}.";

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
            foreach (var channel in new[] { NotificationChannelEnum.Push, NotificationChannelEnum.InApp })
            {
                var cmd = new CreateNotificationCommand
                {
                    UserId = userId,
                    Type = NotificationTypeEnum.BatteryAlertEscalationPending,
                    Channel = channel,
                    Title = title,
                    Body = body,
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
