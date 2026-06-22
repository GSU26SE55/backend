using System.Text.Json;
using MassTransit;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Domain.Enums;
using SharedContracts.Events;

namespace NotificationService.Application.Consumers;

/// <summary>
/// GH-107 — Ticket resolved → notify Customer + Manager (placeholder Guid.Empty).
/// StaffId trong event là người resolve, KHÔNG phải recipient. Ghi trực tiếp qua UnitOfWork (InApp + Push).
/// </summary>
public class TicketResolvedConsumer : IConsumer<TicketResolvedEvent>
{
    private readonly INotificationUnitOfWork _unitOfWork;

    public TicketResolvedConsumer(INotificationUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task Consume(ConsumeContext<TicketResolvedEvent> context)
    {
        var evt = context.Message;

        // TODO Sprint 6 — resolve Customer + Manager user IDs.
        var recipientIds = new[] { Guid.Empty };

        var title = $"Ticket {evt.Code} đã được xử lý";
        var body = string.IsNullOrWhiteSpace(evt.ResolutionSummary)
            ? $"Ticket {evt.Code} đã được resolve."
            : $"Ticket {evt.Code} đã được resolve: {evt.ResolutionSummary}";
        var payload = JsonSerializer.Serialize(new
        {
            ticketId = evt.TicketId,
            code = evt.Code,
            resolvedByStaffId = evt.StaffId,
            screen = "TicketDetail"
        });

        await NotificationWriter.WriteAsync(
            _unitOfWork, recipientIds, NotificationTypeEnum.TicketResolved, NotificationWriter.InAppPush,
            title, body, payload, "Ticket", evt.TicketId, context.CancellationToken);
    }
}
