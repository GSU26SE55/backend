using BatteryService.Application.Interfaces;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using SharedContracts.Saga.AlertTicket;

namespace BatteryService.Infrastructure.Consumers;

/// <summary>
/// BatteryService consumer cho <see cref="LinkAlertToTicketCommand"/> từ Saga.
///
/// Idempotent: nếu Alert.TicketId đã = TicketId expected → trả response success ngay.
/// Nếu Alert.TicketId khác → reject (data conflict — admin reconciliation cần).
///
/// Sprint 5B #238 (xem overall.md §53.7).
/// </summary>
public class LinkAlertToTicketConsumer : IConsumer<LinkAlertToTicketCommand>
{
    private readonly IBatteryUnitOfWork _uow;
    private readonly IIntegrationEventOutboxWriter _outbox;
    private readonly ILogger<LinkAlertToTicketConsumer> _logger;

    public LinkAlertToTicketConsumer(
        IBatteryUnitOfWork uow,
        IIntegrationEventOutboxWriter outbox,
        ILogger<LinkAlertToTicketConsumer> logger)
    {
        _uow = uow;
        _outbox = outbox;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<LinkAlertToTicketCommand> context)
    {
        var msg = context.Message;

        var alert = await _uow.Alerts.GetAllAsync()
            .Where(a => a.Id == msg.AlertId && !a.IsDeleted)
            .FirstOrDefaultAsync(context.CancellationToken);

        if (alert is null)
        {
            _logger.LogWarning(
                "Saga {Correlation}: Alert {AlertId} not found (or soft-deleted). Rejecting link.",
                msg.CorrelationId, msg.AlertId);

            await context.Publish(new AlertLinkToTicketRejected(
                msg.CorrelationId, msg.AlertId, msg.TicketId, "Alert not found", "ALERT_NOT_FOUND"));
            await _outbox.WriteAsync(new AlertLinkToTicketRejectedEvent(
                msg.AlertId, "Alert not found", "ALERT_NOT_FOUND", DateTime.UtcNow),
                context.CancellationToken);
            await _uow.SaveChangesAsync(context.CancellationToken);
            return;
        }

        // Idempotency: already linked to same Ticket.
        if (alert.TicketId == msg.TicketId)
        {
            _logger.LogInformation(
                "Saga {Correlation}: Alert {AlertId} already linked to Ticket {TicketId}. Idempotent ack.",
                msg.CorrelationId, msg.AlertId, msg.TicketId);

            await context.Publish(new AlertLinkedToTicketResponse(
                msg.CorrelationId, msg.AlertId, msg.TicketId,
                alert.UpdatedAt ?? alert.CreatedAt));
            // Idempotent — không re-publish AlertLinkedToTicketEvent (đã publish lần đầu).
            return;
        }

        // Conflict: linked to different Ticket.
        if (alert.TicketId is { } existing && existing != msg.TicketId)
        {
            _logger.LogError(
                "Saga {Correlation}: Alert {AlertId} already linked to different Ticket {ExistingTicketId} (expected {ExpectedTicketId}). Rejecting.",
                msg.CorrelationId, msg.AlertId, existing, msg.TicketId);

            await context.Publish(new AlertLinkToTicketRejected(
                msg.CorrelationId, msg.AlertId, msg.TicketId,
                $"Alert already linked to different Ticket {existing}",
                "ALERT_ALREADY_LINKED_TO_DIFFERENT_TICKET"));
            await _outbox.WriteAsync(new AlertLinkToTicketRejectedEvent(
                msg.AlertId,
                $"Alert already linked to different Ticket {existing}",
                "ALERT_ALREADY_LINKED_TO_DIFFERENT_TICKET",
                DateTime.UtcNow), context.CancellationToken);
            await _uow.SaveChangesAsync(context.CancellationToken);
            return;
        }

        // Happy path: link the alert + publish integration event for downstream subscribers.
        alert.TicketId = msg.TicketId;
        _uow.Alerts.UpdateAsync(alert);

        await _outbox.WriteAsync(new AlertLinkedToTicketEvent(
            AlertId: msg.AlertId,
            TicketId: msg.TicketId,
            TicketCode: msg.TicketCode,
            IsReused: false,
            LinkedAt: DateTime.UtcNow), context.CancellationToken);

        await _uow.SaveChangesAsync(context.CancellationToken);

        await context.Publish(new AlertLinkedToTicketResponse(
            msg.CorrelationId, msg.AlertId, msg.TicketId, DateTime.UtcNow));
    }
}
