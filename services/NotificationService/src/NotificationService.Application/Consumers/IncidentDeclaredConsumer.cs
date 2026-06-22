using System.Text.Json;
using MassTransit;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Domain.Enums;
using SharedContracts.Events;

namespace NotificationService.Application.Consumers;

/// <summary>
/// GH-107 — Manager declare P1 Critical Incident từ Ticket → notify Manager + Admin.
/// DeclaredByUserId là người declare, KHÔNG phải recipient. Ghi trực tiếp qua UnitOfWork (InApp + Push).
/// </summary>
public class IncidentDeclaredConsumer : IConsumer<IncidentDeclaredEvent>
{
    private readonly INotificationUnitOfWork _unitOfWork;

    public IncidentDeclaredConsumer(INotificationUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task Consume(ConsumeContext<IncidentDeclaredEvent> context)
    {
        var evt = context.Message;

        // TODO Sprint 6 — resolve Manager + Admin user IDs.
        var recipientIds = new[] { Guid.Empty };

        var title = $"🚨 Critical Incident: {evt.Code}";
        var body = $"Ticket {evt.Code} đã được declare thành Critical Incident. Cần xử lý khẩn.";
        var payload = JsonSerializer.Serialize(new
        {
            ticketId = evt.TicketId,
            code = evt.Code,
            declaredByUserId = evt.DeclaredByUserId,
            screen = "TicketDetail"
        });

        await NotificationWriter.WriteAsync(
            _unitOfWork, recipientIds, NotificationTypeEnum.IncidentDeclared, NotificationWriter.InAppPush,
            title, body, payload, "Ticket", evt.TicketId, context.CancellationToken);
    }
}
