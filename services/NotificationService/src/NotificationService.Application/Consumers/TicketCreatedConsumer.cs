using System.Text.Json;
using MassTransit;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Domain.Enums;
using SharedContracts.Events;

namespace NotificationService.Application.Consumers;

/// <summary>
/// GH-107 — Ticket mới tạo → notify Manager (placeholder Guid.Empty cho tới khi có recipient resolve
/// Sprint 6 — AccountSyncReadModel). Ghi notification trực tiếp qua UnitOfWork (InApp + Push).
/// </summary>
public class TicketCreatedConsumer : IConsumer<TicketCreatedEvent>
{
    private readonly INotificationUnitOfWork _unitOfWork;

    public TicketCreatedConsumer(INotificationUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task Consume(ConsumeContext<TicketCreatedEvent> context)
    {
        var evt = context.Message;

        // TODO Sprint 6 — resolve Manager user IDs.
        var recipientIds = new[] { Guid.Empty };

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
