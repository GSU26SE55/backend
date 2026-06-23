using System.Text.Json;
using MassTransit;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Domain.Enums;
using SharedContracts.Events;

namespace NotificationService.Application.Consumers;

/// <summary>
/// GH-107 — Ticket mới tạo → notify Manager. GH-604: recipient resolve qua <see cref="IRecipientResolver"/>
/// (broadcast toàn bộ Manager). Ghi notification trực tiếp qua UnitOfWork (InApp + Push).
/// </summary>
public class TicketCreatedConsumer : IConsumer<TicketCreatedEvent>
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly IRecipientResolver _recipientResolver;
    private readonly ILogger<TicketCreatedConsumer> _logger;

    public TicketCreatedConsumer(
        INotificationUnitOfWork unitOfWork,
        IRecipientResolver recipientResolver,
        ILogger<TicketCreatedConsumer> logger)
    {
        _unitOfWork = unitOfWork;
        _recipientResolver = recipientResolver;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TicketCreatedEvent> context)
    {
        var evt = context.Message;

        var recipientIds = await _recipientResolver.GetActiveByRoleAsync(context.CancellationToken, "Manager");
        if (recipientIds.Count == 0)
        {
            _logger.LogWarning("No Manager recipient resolved for TicketCreated ticket={TicketId} — skip.", evt.TicketId);
            return;
        }

        var title = $"Ticket mới: {evt.Code}";
        var body = $"Ticket {evt.Code} vừa được tạo và đang chờ phân công.";
        var payload = JsonSerializer.Serialize(new
        {
            ticketId = evt.TicketId,
            code = evt.Code,
            screen = "TicketDetail"
        });

        await NotificationWriter.WriteAsync(
            _unitOfWork, recipientIds, NotificationTypeEnum.TicketCreated, NotificationWriter.InAppPush,
            title, body, payload, "Ticket", evt.TicketId, context.CancellationToken);
    }
}
