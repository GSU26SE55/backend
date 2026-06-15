using System.Text.Json;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using NotificationService.Application.CQRS.Command.Notification;
using NotificationService.Domain.Enums;
using SharedContracts.Saga.AlertTicket;

namespace NotificationService.Application.Consumers;

/// <summary>
/// Consumer cho <see cref="AlertTicketSagaFailedEvent"/>:
/// notify Admin (primary) + Manager (CC) khi Alert-Ticket Saga vào terminal Failed.
/// Admin reprocess via <c>POST /api/admin/sagas/alert-ticket/{alertId}/reprocess</c>.
///
/// Notification debounce 5 phút per AlertId.
///
/// Sprint 5B #238 (xem overall.md §53.11).
/// </summary>
public class AlertTicketSagaFailedConsumer : IConsumer<AlertTicketSagaFailedEvent>
{
    private readonly IMediator _mediator;
    private readonly ILogger<AlertTicketSagaFailedConsumer> _logger;

    public AlertTicketSagaFailedConsumer(
        IMediator mediator,
        ILogger<AlertTicketSagaFailedConsumer> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<AlertTicketSagaFailedEvent> context)
    {
        var evt = context.Message;

        // TODO #238: query Admin user IDs (and Manager nếu phân quyền cho Saga reprocess view).
        // Placeholder: emit for AdminBroadcast user.
        var recipientIds = new[] { Guid.Empty };

        var title = $"[Saga Failed] Alert {evt.AlertId} — {evt.FailedAtStage}";
        var body = $"Alert-Ticket Saga failed at stage '{evt.FailedAtStage}': {evt.Reason}. " +
                   $"Admin reprocess required. Asset: {evt.AssetSerialNumber}";

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
                    Body = body,
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
