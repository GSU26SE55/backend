using System.Text.Json;
using MassTransit;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Domain.Enums;
using SharedContracts.Events;

namespace NotificationService.Application.Consumers;

/// <summary>
/// GH-107 — Ticket bị escalate (SLA breach hoặc Staff/Manager request) → notify Manager + Admin.
/// Reason là int (serialize từ EscalationReasonEnum) — không reference TicketService.Domain.
/// Ghi trực tiếp qua UnitOfWork (InApp + Push).
/// </summary>
public class TicketEscalatedConsumer : IConsumer<TicketEscalatedEvent>
{
    private readonly INotificationUnitOfWork _unitOfWork;

    public TicketEscalatedConsumer(INotificationUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task Consume(ConsumeContext<TicketEscalatedEvent> context)
    {
        var evt = context.Message;

        // TODO Sprint 6 — resolve Manager + Admin user IDs.
        var recipientIds = new[] { Guid.Empty };

        var title = $"Ticket {evt.Code} đã escalate";
        var body = string.IsNullOrWhiteSpace(evt.Note)
            ? $"Ticket {evt.Code} vừa được escalate (lý do #{evt.Reason})."
            : $"Ticket {evt.Code} vừa được escalate (lý do #{evt.Reason}): {evt.Note}";
        var payload = JsonSerializer.Serialize(new
        {
            ticketId = evt.TicketId,
            code = evt.Code,
            reason = evt.Reason,
            note = evt.Note,
            staffId = evt.StaffId,
            staffName = evt.StaffName,
            screen = "TicketDetail"
        });

        await NotificationWriter.WriteAsync(
            _unitOfWork, recipientIds, NotificationTypeEnum.TicketEscalated, NotificationWriter.InAppPush,
            title, body, payload, "Ticket", evt.TicketId, context.CancellationToken);
    }
}
