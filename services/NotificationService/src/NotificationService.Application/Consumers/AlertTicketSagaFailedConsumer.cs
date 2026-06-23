using System.Text.Json;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using NotificationService.Application.CQRS.Command.Notification;
using NotificationService.Application.Services;
using NotificationService.Application.Templates;
using NotificationService.Domain.Enums;
using SharedContracts.Interfaces;
using SharedContracts.Saga.AlertTicket;

namespace NotificationService.Application.Consumers;

/// <summary>
/// Consumer cho <see cref="AlertTicketSagaFailedEvent"/>:
/// notify Admin (primary) + Manager (CC) khi Alert-Ticket Saga vào terminal Failed.
/// Admin reprocess via <c>POST /api/admin/sagas/alert-ticket/{alertId}/reprocess</c>.
///
/// GH-593 — Notification debounce 5 phút per AlertId (qua <see cref="NotificationDebounce"/> + ICacheService).
///
/// Sprint 5B #238 (xem overall.md §53.11).
/// </summary>
public class AlertTicketSagaFailedConsumer : IConsumer<AlertTicketSagaFailedEvent>
{
    private readonly IMediator _mediator;
    private readonly ITemplateRenderer _templateRenderer;
    private readonly ICacheService _cache;
    private readonly IRecipientResolver _recipientResolver;
    private readonly ILogger<AlertTicketSagaFailedConsumer> _logger;

    public AlertTicketSagaFailedConsumer(
        IMediator mediator,
        ITemplateRenderer templateRenderer,
        ICacheService cache,
        IRecipientResolver recipientResolver,
        ILogger<AlertTicketSagaFailedConsumer> logger)
    {
        _mediator = mediator;
        _templateRenderer = templateRenderer;
        _cache = cache;
        _recipientResolver = recipientResolver;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<AlertTicketSagaFailedEvent> context)
    {
        var evt = context.Message;

        // GH-593 — debounce 5 phút per AlertId: bỏ qua event trùng trong cửa sổ.
        if (!await NotificationDebounce.TryBeginAsync(_cache, evt.AlertId, context.CancellationToken))
        {
            _logger.LogInformation(
                "Debounce: skip duplicate saga-failed notification AlertId={AlertId}", evt.AlertId);
            return;
        }

        // GH-604: recipient resolve qua read-model (broadcast Admin + Manager).
        var recipientIds = await _recipientResolver.GetActiveByRoleAsync(context.CancellationToken, "Admin", "Manager");
        if (recipientIds.Count == 0)
        {
            _logger.LogWarning("No Admin/Manager recipient resolved for AlertTicketSagaFailed AlertId={AlertId} — skip.", evt.AlertId);
            return;
        }

        var title = $"[Saga Failed] Alert {evt.AlertId} — {evt.FailedAtStage}";
        var plainBody = $"Alert-Ticket Saga failed at stage '{evt.FailedAtStage}': {evt.Reason}. " +
                        $"Admin reprocess required. Asset: {evt.AssetSerialNumber}";

        var htmlBody = _templateRenderer.Render("alert-ticket-saga-failed", new
        {
            CorrelationId = evt.CorrelationId,
            AlertId = evt.AlertId,
            TicketId = evt.TicketId?.ToString(),
            AssetSerialNumber = evt.AssetSerialNumber,
            FailedAtStage = evt.FailedAtStage,
            Reason = evt.Reason,
            ErrorCode = evt.ErrorCode,
            FailedAt = evt.FailedAt.ToString("dd/MM/yyyy HH:mm:ss")
        });

        var payload = JsonSerializer.Serialize(new
        {
            correlationId = evt.CorrelationId,
            alertId = evt.AlertId,
            ticketId = evt.TicketId,
            failedAtStage = evt.FailedAtStage,
            errorCode = evt.ErrorCode,
            failedAt = evt.FailedAt
        });

        foreach (var userId in recipientIds)
        {
            foreach (var channel in new[] { NotificationChannelEnum.Push, NotificationChannelEnum.Email, NotificationChannelEnum.InApp })
            {
                var cmd = new CreateNotificationCommand
                {
                    UserId = userId,
                    Type = NotificationTypeEnum.AlertTicketSagaFailed,
                    Channel = channel,
                    Title = title,
                    Body = channel == NotificationChannelEnum.Email ? htmlBody : plainBody,
                    PayloadJson = payload,
                    EntityType = "AlertTicketSaga",
                    EntityId = evt.AlertId
                };

                var result = await _mediator.Send(cmd, context.CancellationToken);
                if (!result.IsSuccess)
                {
                    _logger.LogWarning(
                        "Failed to create saga-failed notification for AlertId={AlertId}: {Message}",
                        evt.AlertId, result.Message);
                }
            }
        }
    }
}
