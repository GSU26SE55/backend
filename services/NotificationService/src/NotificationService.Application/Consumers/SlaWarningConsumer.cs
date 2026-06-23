using System.Globalization;
using System.Text.Json;
using MassTransit;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Domain.Enums;
using SharedContracts.Events;

namespace NotificationService.Application.Consumers;

/// <summary>
/// GH-107 — SLA timer sắp hết (warning threshold) → notify Manager. GH-604: recipient resolve qua
/// <see cref="IRecipientResolver"/> (broadcast Manager). Staff phụ trách cụ thể bị DEFER
/// (event chỉ mang TicketId, không có AssignedStaffId) — follow-up enrich event sau.
/// Ghi trực tiếp qua UnitOfWork (InApp + Push).
/// </summary>
public class SlaWarningConsumer : IConsumer<SlaWarningEvent>
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly IRecipientResolver _recipientResolver;
    private readonly ILogger<SlaWarningConsumer> _logger;

    public SlaWarningConsumer(
        INotificationUnitOfWork unitOfWork,
        IRecipientResolver recipientResolver,
        ILogger<SlaWarningConsumer> logger)
    {
        _unitOfWork = unitOfWork;
        _recipientResolver = recipientResolver;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<SlaWarningEvent> context)
    {
        var evt = context.Message;

        var recipientIds = await _recipientResolver.GetActiveByRoleAsync(context.CancellationToken, "Manager");
        if (recipientIds.Count == 0)
        {
            _logger.LogWarning("No Manager recipient resolved for SlaWarning ticket={TicketId} — skip.", evt.TicketId);
            return;
        }

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
