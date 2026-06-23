using System.Text.Json;
using MassTransit;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Domain.Enums;
using SharedContracts.Events;

namespace NotificationService.Application.Consumers;

/// <summary>
/// GH-107 — SLA timer đã breach → notify Manager + Admin. GH-604: recipient resolve qua
/// <see cref="IRecipientResolver"/> (broadcast Manager + Admin). Ghi trực tiếp qua UnitOfWork (InApp + Push).
/// </summary>
public class SlaBreachedConsumer : IConsumer<SlaBreachedEvent>
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly IRecipientResolver _recipientResolver;
    private readonly ILogger<SlaBreachedConsumer> _logger;

    public SlaBreachedConsumer(
        INotificationUnitOfWork unitOfWork,
        IRecipientResolver recipientResolver,
        ILogger<SlaBreachedConsumer> logger)
    {
        _unitOfWork = unitOfWork;
        _recipientResolver = recipientResolver;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<SlaBreachedEvent> context)
    {
        var evt = context.Message;

        var recipientIds = await _recipientResolver.GetActiveByRoleAsync(context.CancellationToken, "Manager", "Admin");
        if (recipientIds.Count == 0)
        {
            _logger.LogWarning("No Manager/Admin recipient resolved for SlaBreached ticket={TicketId} — skip.", evt.TicketId);
            return;
        }

        var title = "🔴 SLA đã bị vi phạm";
        var body = $"Ticket (ưu tiên {evt.Priority}) đã breach SLA lúc {evt.BreachedAt:dd/MM HH:mm}. Cần escalate thêm nhân lực.";
        var payload = JsonSerializer.Serialize(new
        {
            ticketId = evt.TicketId,
            breachedAt = evt.BreachedAt,
            priority = evt.Priority,
            screen = "TicketDetail"
        });

        await NotificationWriter.WriteAsync(
            _unitOfWork, recipientIds, NotificationTypeEnum.SlaBreached, NotificationWriter.InAppPush,
            title, body, payload, "Ticket", evt.TicketId, context.CancellationToken);
    }
}
