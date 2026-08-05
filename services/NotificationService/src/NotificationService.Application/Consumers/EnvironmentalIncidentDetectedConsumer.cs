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
/// Sprint IoT-2 #IoT2-31 (S6-BE-05) — Smoke/Water/EnvAnomaly incident → page Manager + Admin.
/// GH-604: recipient resolve qua <see cref="IRecipientResolver"/> (broadcast Manager + Admin).
///
/// Routing (overall.md §3.4 + §49.3):
/// <list type="bullet">
///   <item><b>Push</b> + <b>Email</b> + <b>SMS</b> (Critical channel).</item>
///   <item><b>BypassQuietHours = true</b> — Critical incident phải đánh thức người trực ngoài giờ.</item>
/// </list>
///
/// Push template (§15.2):
/// Title: 🚨 Sự cố môi trường: {IncidentType} tại {SiteName}
/// Body:  Severity {Severity} — {Description}. Phát hiện lúc {DetectedAt:HH:mm}. Xử lý ngay!
/// </summary>
public class EnvironmentalIncidentDetectedConsumer : IConsumer<EnvironmentalIncidentDetectedEvent>
{
    private readonly IMediator _mediator;
    private readonly ITemplateRenderer _templateRenderer;
    private readonly IRecipientResolver _recipientResolver;
    private readonly ICacheService _cache;
    private readonly ILogger<EnvironmentalIncidentDetectedConsumer> _logger;

    public EnvironmentalIncidentDetectedConsumer(
        IMediator mediator,
        ITemplateRenderer templateRenderer,
        IRecipientResolver recipientResolver,
        ICacheService cache,
        ILogger<EnvironmentalIncidentDetectedConsumer> logger)
    {
        _mediator = mediator;
        _templateRenderer = templateRenderer;
        _recipientResolver = recipientResolver;
        _cache = cache;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<EnvironmentalIncidentDetectedEvent> context)
    {
        // GH-765 — chỗ giữ có hạn ngắn, chỉ nâng lên cửa sổ 30 phút SAU KHI ghi xong.
        // Bản cũ chiếm key 30 phút ngay từ đầu, nên một lỗi DB/resolver ở lần đầu là mọi lần
        // gửi lại trong 30 phút đều bị coi là trùng ⇒ notification biến mất hẳn.
        await NotificationDebounce.ProcessOnceAsync(_cache, context, "EnvironmentalIncidentDetected", _logger, async () =>
        {
            var evt = context.Message;

            // GH-604: recipient resolve qua read-model (broadcast Manager + Admin).
            var recipientIds = await _recipientResolver.GetActiveByRoleAsync(context.CancellationToken, "Manager", "Admin");
            if (recipientIds.Count == 0)
            {
                _logger.LogWarning("No Manager/Admin recipient resolved for EnvironmentalIncidentDetected incident={IncidentId} — skip.", evt.IncidentId);
                return;
            }

            // Push title/body — §15.2 EnvironmentalIncidentCritical format
            var title = $"🚨 Sự cố môi trường: {evt.IncidentType} tại {evt.SiteName}";
            var plainBody = $"Severity {evt.Severity} — {evt.Description ?? string.Empty}. " +
                            $"Phát hiện lúc {evt.DetectedAt:HH:mm}. Xử lý ngay!";

            var htmlBody = _templateRenderer.Render("environmental-incident-detected", new
            {
                SiteName = evt.SiteName,
                IncidentType = evt.IncidentType.ToString(),
                Severity = evt.Severity.ToString(),
                Description = evt.Description,
                DetectedAt = evt.DetectedAt.ToString("dd/MM/yyyy HH:mm:ss"),
                IncidentId = evt.IncidentId.ToString()
            });

            var payload = JsonSerializer.Serialize(new
            {
                incidentId = evt.IncidentId,
                siteId = evt.SiteId,
                siteName = evt.SiteName,
                incidentType = evt.IncidentType,
                severity = evt.Severity,
                alertId = evt.AlertId,
                customerId = evt.CustomerId,
                detectedAt = evt.DetectedAt,
                description = evt.Description,
                screen = "IncidentDetail"
            });

            // Push + Email + SMS, BypassQuietHours = true (Critical bypass).
            var channels = new[]
            {
                // Sprint 6.3 NOTI3-01 (#701) — InApp bắt buộc: feed lọc theo Channel=InApp,
                // thiếu row này thì sự cố môi trường không hiện trong app (R-40).
                NotificationChannelEnum.InApp,
                NotificationChannelEnum.Push,
                NotificationChannelEnum.Email,
                NotificationChannelEnum.Sms
            };

            foreach (var userId in recipientIds)
            {
                foreach (var channel in channels)
                {
                    var cmd = new CreateNotificationCommand
                    {
                        UserId = userId,
                        Type = NotificationTypeEnum.EnvironmentalIncidentDetected,
                        Channel = channel,
                        Title = title,
                        Body = channel == NotificationChannelEnum.Email ? htmlBody : plainBody,
                        PayloadJson = payload,
                        EntityType = "EnvironmentalIncident",
                        EntityId = evt.IncidentId,
                        BypassQuietHours = true
                    };
                    var result = await _mediator.Send(cmd, context.CancellationToken);
                    if (!result.IsSuccess)
                    {
                        _logger.LogWarning(
                            "Failed to create EnvironmentalIncident notification incident={IncidentId} channel={Channel}: {Message}",
                            evt.IncidentId, channel, result.Message);
                    }
                }
            }
        });
    }
}
