using System.Globalization;
using System.Text.Json;
using MassTransit;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Domain.Enums;
using SharedContracts.Events;

namespace NotificationService.Application.Consumers;

/// <summary>
/// GH-107 — SLA timer sắp hết (warning threshold) → notify Staff phụ trách + Manager.
/// Ghi trực tiếp qua UnitOfWork (InApp + Push).
/// </summary>
public class SlaWarningConsumer : IConsumer<SlaWarningEvent>
{
    private readonly INotificationUnitOfWork _unitOfWork;

    public SlaWarningConsumer(INotificationUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task Consume(ConsumeContext<SlaWarningEvent> context)
    {
        var evt = context.Message;

        // TODO Sprint 6 — resolve Staff phụ trách + Manager user IDs.
        var recipientIds = new[] { Guid.Empty };

        var pct = evt.Percentage.ToString("0.#", CultureInfo.InvariantCulture);
        var title = "⏰ Cảnh báo SLA sắp hết hạn";
        var body = $"Ticket đã dùng {pct}% thời gian SLA (hạn {evt.WarningAt:dd/MM HH:mm}). Cần xử lý sớm.";
        var payload = JsonSerializer.Serialize(new
        {
            ticketId = evt.TicketId,
            warningAt = evt.WarningAt,
            percentage = evt.Percentage,
            screen = "TicketDetail"
        });

        await NotificationWriter.WriteAsync(
            _unitOfWork, recipientIds, NotificationTypeEnum.SlaWarning, NotificationWriter.InAppPush,
            title, body, payload, "Ticket", evt.TicketId, context.CancellationToken);
    }
}
