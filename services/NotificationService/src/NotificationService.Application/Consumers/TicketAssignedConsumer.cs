using System.Text.Json;
using MassTransit;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Domain.Enums;
using SharedContracts.Events;

namespace NotificationService.Application.Consumers;

/// <summary>
/// GH-107 — Ticket được assign/reassign Staff → notify chính Staff đó (StaffId có sẵn trong event).
/// Ghi notification trực tiếp qua UnitOfWork (InApp + Push).
/// </summary>
public class TicketAssignedConsumer : IConsumer<TicketAssignedEvent>
{
    private readonly INotificationUnitOfWork _unitOfWork;

    public TicketAssignedConsumer(INotificationUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task Consume(ConsumeContext<TicketAssignedEvent> context)
    {
        var evt = context.Message;

        var recipientIds = new[] { evt.StaffId };

        var title = $"Bạn được phân công ticket {evt.Code}";
        var body = $"Ticket {evt.Code} (ưu tiên {evt.Priority}) đã được giao cho bạn.";
        var payload = JsonSerializer.Serialize(new
        {
            ticketId = evt.TicketId,
            code = evt.Code,
            staffId = evt.StaffId,
            priority = evt.Priority,
            screen = "TicketDetail"
        });

        await NotificationWriter.WriteAsync(
            _unitOfWork, recipientIds, NotificationTypeEnum.TicketAssigned, NotificationWriter.InAppPush,
            title, body, payload, "Ticket", evt.TicketId, context.CancellationToken);
    }
}
