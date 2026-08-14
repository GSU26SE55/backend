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
/// GH-107 — Ticket mới tạo → notify Manager. GH-604: recipient resolve qua <see cref="IRecipientResolver"/>
/// (broadcast toàn bộ Manager). Ghi notification trực tiếp qua UnitOfWork (InApp + Push).
/// </summary>
public class TicketCreatedConsumer : IConsumer<TicketCreatedEvent>
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly IRecipientResolver _recipientResolver;
    private readonly ICacheService _cache;
    private readonly ILogger<TicketCreatedConsumer> _logger;

    public TicketCreatedConsumer(
        INotificationUnitOfWork unitOfWork,
        IRecipientResolver recipientResolver,
        ICacheService cache,
        ILogger<TicketCreatedConsumer> logger)
    {
        _unitOfWork = unitOfWork;
        _recipientResolver = recipientResolver;
        _cache = cache;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TicketCreatedEvent> context)
    {
        // GH-765 — chỗ giữ có hạn ngắn, chỉ nâng lên cửa sổ 30 phút SAU KHI ghi xong.
        // Bản cũ chiếm key 30 phút ngay từ đầu, nên một lỗi DB/resolver ở lần đầu là mọi lần
        // gửi lại trong 30 phút đều bị coi là trùng ⇒ notification biến mất hẳn.
        await NotificationDebounce.ProcessOnceAsync(_cache, context, "TicketCreated", _logger, async () =>
        {
            var evt = context.Message;

            var recipientIds = await _recipientResolver.GetActiveByRoleAsync(context.CancellationToken, "Manager");
            if (recipientIds.Count == 0)
            {
                _logger.LogWarning("No Manager recipient resolved for TicketCreated ticket={TicketId} — skip.", evt.TicketId);
                return;
            }

            // Sprint 6.2 NOTI-05 (#676) — payload nay có Priority nên Manager biết ngay ticket ưu tiên gì.
            var title = string.IsNullOrWhiteSpace(evt.Priority)
                ? $"New ticket: {evt.Code}"
                : $"New ticket: {evt.Code} (priority {evt.Priority})";
            var body = string.IsNullOrWhiteSpace(evt.Priority)
                ? $"Ticket {evt.Code} has just been created and is awaiting triage."
                : $"Ticket {evt.Code} (priority {evt.Priority}) has just been created and is awaiting assignment.";
            var payload = JsonSerializer.Serialize(new
            {
                ticketId = evt.TicketId,
                code = evt.Code,
                customerId = evt.CustomerId,
                priority = evt.Priority,
                screen = "TicketDetail"
            });

            await NotificationWriter.WriteAsync(
                _unitOfWork, recipientIds, NotificationTypeEnum.TicketCreated, NotificationWriter.InAppPush,
                title, body, payload, "Ticket", evt.TicketId, context.CancellationToken);
        });
    }
}
