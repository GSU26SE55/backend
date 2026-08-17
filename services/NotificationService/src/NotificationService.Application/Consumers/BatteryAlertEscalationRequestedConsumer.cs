using System.Text.Json;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using NotificationService.Application.CQRS.Command.Notification;
using NotificationService.Application.Services;
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
    private readonly IRecipientResolver _recipientResolver;
    private readonly ILogger<BatteryAlertEscalationRequestedConsumer> _logger;

    public BatteryAlertEscalationRequestedConsumer(
        IMediator mediator,
        ITemplateRenderer templateRenderer,
        ICacheService cache,
        IRecipientResolver recipientResolver,
        ILogger<BatteryAlertEscalationRequestedConsumer> logger)
    {
        _mediator = mediator;
        _templateRenderer = templateRenderer;
        _cache = cache;
        _recipientResolver = recipientResolver;
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

        // GH-604: recipient resolve qua read-model (broadcast Manager + Admin).
        var recipientIds = await _recipientResolver.GetActiveByRoleAsync(context.CancellationToken, "Manager", "Admin");
        if (recipientIds.Count == 0)
        {
            _logger.LogWarning("No Manager/Admin recipient resolved for BatteryAlertEscalation AlertId={AlertId} — skip.", evt.AlertId);
            return;
        }

        // Bỏ tiếng Anh và định dạng ngày ISO (`:O` in ra 2026-08-03T14:15:00.0000000Z — không ai đọc).
        // "ack" cũng là từ nội bộ. ActualValue/Unit nullable (alert theo sự cố không có số đo) nên
        // chỉ ghép mệnh đề giá trị khi thực sự có, tránh in ra "Giá trị đo:  ." cụt lủn.
        var measured = evt.ActualValue.HasValue
            ? $" Measured value: {evt.ActualValue}{(string.IsNullOrWhiteSpace(evt.Unit) ? "" : $" {evt.Unit}")}."
            : string.Empty;

        var title = $"Battery alert {evt.AssetSerialNumber} has not been handled";
        var plainBody = $"Critical alert on battery {evt.AssetSerialNumber} detected at " +
                        $"{evt.DetectedAt:HH:mm dd/MM/yyyy}, unacknowledged for {evt.MinutesSinceDetection} minutes." +
                        $"{measured} Immediate review required.";

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
