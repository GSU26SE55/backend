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
/// GH-107 — Ticket bị escalate (SLA breach hoặc Staff/Manager request) → notify Manager + Admin.
/// GH-604: recipient resolve qua <see cref="IRecipientResolver"/> (broadcast Manager + Admin).
/// Reason là int (serialize từ EscalationReasonEnum) — không reference TicketService.Domain.
/// Ghi trực tiếp qua UnitOfWork (InApp + Push).
/// </summary>
public class TicketEscalatedConsumer : IConsumer<TicketEscalatedEvent>
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly IRecipientResolver _recipientResolver;
    private readonly ICacheService _cache;
    private readonly ILogger<TicketEscalatedConsumer> _logger;

    public TicketEscalatedConsumer(
        INotificationUnitOfWork unitOfWork,
        IRecipientResolver recipientResolver,
        ICacheService cache,
        ILogger<TicketEscalatedConsumer> logger)
    {
        _unitOfWork = unitOfWork;
        _recipientResolver = recipientResolver;
        _cache = cache;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TicketEscalatedEvent> context)
    {
        // GH-765 — chỗ giữ có hạn ngắn, chỉ nâng lên cửa sổ 30 phút SAU KHI ghi xong.
        // Bản cũ chiếm key 30 phút ngay từ đầu, nên một lỗi DB/resolver ở lần đầu là mọi lần
        // gửi lại trong 30 phút đều bị coi là trùng ⇒ notification biến mất hẳn.
        await NotificationDebounce.ProcessOnceAsync(_cache, context, "TicketEscalated", _logger, async () =>
        {
            var evt = context.Message;

            var recipientIds = await _recipientResolver.GetActiveByRoleAsync(context.CancellationToken, "Manager", "Admin");
            if (recipientIds.Count == 0)
            {
                _logger.LogWarning("No Manager/Admin recipient resolved for TicketEscalated ticket={TicketId} — skip.", evt.TicketId);
                return;
            }

            var title = $"Ticket {evt.Code} escalated";
            var body = string.IsNullOrWhiteSpace(evt.Note)
                ? $"Ticket {evt.Code} has just been escalated (reason #{evt.Reason})."
                : $"Ticket {evt.Code} has just been escalated (reason #{evt.Reason}): {evt.Note}";
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
        });
    }
}
