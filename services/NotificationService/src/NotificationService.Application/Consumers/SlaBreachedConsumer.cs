using System.Text.Json;
using MassTransit;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Domain.Enums;
using SharedContracts.Events;

namespace NotificationService.Application.Consumers;

/// <summary>
/// GH-107 — SLA timer đã breach → notify Manager + Admin. Ghi trực tiếp qua UnitOfWork (InApp + Push).
/// </summary>
public class SlaBreachedConsumer : IConsumer<SlaBreachedEvent>
{
    private readonly INotificationUnitOfWork _unitOfWork;

    public SlaBreachedConsumer(INotificationUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task Consume(ConsumeContext<SlaBreachedEvent> context)
    {
        var evt = context.Message;

        // TODO Sprint 6 — resolve Manager + Admin user IDs.
        var recipientIds = new[] { Guid.Empty };

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
