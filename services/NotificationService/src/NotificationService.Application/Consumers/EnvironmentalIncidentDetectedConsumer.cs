using System.Text.Json;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using NotificationService.Application.CQRS.Command.Notification;
using NotificationService.Application.Templates;
using NotificationService.Domain.Enums;
using SharedContracts.Events;

namespace NotificationService.Application.Consumers;

/// <summary>
/// Sprint IoT-2 #IoT2-31 (S6-BE-05) — Smoke/Water/EnvAnomaly incident → page Manager + Admin.
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
    private readonly ILogger<EnvironmentalIncidentDetectedConsumer> _logger;

    public EnvironmentalIncidentDetectedConsumer(
        IMediator mediator,
        ITemplateRenderer templateRenderer,
        ILogger<EnvironmentalIncidentDetectedConsumer> logger)
    {
        _mediator = mediator;
        _templateRenderer = templateRenderer;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<EnvironmentalIncidentDetectedEvent> context)
    {
        var evt = context.Message;

        // TODO Sprint 6 — query Manager/Admin user IDs cho SiteId qua AccountSyncReadModel.
        // Hiện tại: broadcast (placeholder Guid.Empty) — dispatcher xử lý fan-out.
        var recipientIds = new[] { Guid.Empty };

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
    }
}

/// <summary>
/// Sprint IoT-2 #IoT2-31 (paired) — resolved event để clear in-app banner.
/// Channel chỉ InApp (không spam Push/SMS lúc đã xử lý).
/// </summary>
public class EnvironmentalIncidentResolvedConsumer : IConsumer<EnvironmentalIncidentResolvedEvent>
{
    private readonly IMediator _mediator;
    private readonly ILogger<EnvironmentalIncidentResolvedConsumer> _logger;

    public EnvironmentalIncidentResolvedConsumer(IMediator mediator, ILogger<EnvironmentalIncidentResolvedConsumer> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<EnvironmentalIncidentResolvedEvent> context)
    {
        var evt = context.Message;
        var recipientIds = new[] { Guid.Empty };

        var label = evt.WasFalseAlarm ? "false-alarm" : "resolved";
        var title = $"[ENV] Đã {label} — site {evt.SiteId}";
        var body = $"Sự cố môi trường (IncidentId {evt.IncidentId}) đã được đánh dấu {label} lúc {evt.ResolvedAt:O}. " +
                   $"{evt.ResolutionNote ?? string.Empty}";

        foreach (var userId in recipientIds)
        {
            var cmd = new CreateNotificationCommand
            {
                UserId = userId,
                Type = NotificationTypeEnum.EnvironmentalIncidentResolved,
                Channel = NotificationChannelEnum.InApp,
                Title = title,
                Body = body,
                EntityType = "EnvironmentalIncident",
                EntityId = evt.IncidentId
            };
            var result = await _mediator.Send(cmd, context.CancellationToken);
            if (!result.IsSuccess)
            {
                _logger.LogWarning(
                    "Failed to create EnvironmentalIncident resolved notification incident={IncidentId}: {Message}",
                    evt.IncidentId, result.Message);
            }
        }
    }
}
