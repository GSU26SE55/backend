using System.Globalization;
using System.Text.Json;
using MassTransit;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Domain.Enums;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace NotificationService.Application.Consumers;

/// <summary>
/// GH-107 — SLA timer sắp hết (warning threshold) → notify Manager. GH-604: recipient resolve qua
/// <see cref="IRecipientResolver"/> (broadcast Manager).
///
/// Sprint 6.2 NOTI-05 (#676) — <c>SlaWarningEvent</c> nay mang <c>StaffId</c> nên Staff đang phụ
/// trách cũng được báo, đúng spec §3.4 (trước đó phải defer vì payload thiếu StaffId).
/// Ghi trực tiếp qua UnitOfWork (InApp + Push).
/// </summary>
public class SlaWarningConsumer : IConsumer<SlaWarningEvent>
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly IRecipientResolver _recipientResolver;
    private readonly ICacheService _cache;
    private readonly ILogger<SlaWarningConsumer> _logger;

    public SlaWarningConsumer(
        INotificationUnitOfWork unitOfWork,
        IRecipientResolver recipientResolver,
        ICacheService cache,
        ILogger<SlaWarningConsumer> logger)
    {
        _unitOfWork = unitOfWork;
        _recipientResolver = recipientResolver;
        _cache = cache;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<SlaWarningEvent> context)
    {
        var messageId = context.MessageId ?? Guid.Empty;
        if (messageId != Guid.Empty && !await NotificationDebounce.TryBeginByMessageAsync(_cache, messageId, context.CancellationToken))
        {
            _logger.LogInformation("Debounce: skip duplicate SlaWarning message={MessageId}", messageId);
            return;
        }

        var evt = context.Message;

        var recipientIds = (await _recipientResolver.GetActiveByRoleAsync(context.CancellationToken, "Manager")).ToList();

        // Sprint 6.2 NOTI-05 (#676) — spec §3.4 yêu cầu báo CẢ Staff đang phụ trách, không chỉ Manager.
        if (evt.StaffId is { } staffId && staffId != Guid.Empty)
            recipientIds.Add(staffId);

        if (recipientIds.Count == 0)
        {
            _logger.LogWarning("No recipient resolved for SlaWarning ticket={TicketId} — skip.", evt.TicketId);
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
            staffId = evt.StaffId,
            screen = "TicketDetail"
        });

        await NotificationWriter.WriteAsync(
            _unitOfWork, recipientIds.Distinct().ToList(), NotificationTypeEnum.SlaWarning, NotificationWriter.InAppPush,
            title, body, payload, "Ticket", evt.TicketId, context.CancellationToken);
    }
}
