using System.Text.Json;
using MassTransit;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Domain.Enums;
using SharedContracts.Events.Chats;
using SharedContracts.Interfaces;

namespace NotificationService.Application.Consumers;

/// <summary>
/// Sprint 6.2 NOTI-03 (#674) — mắt xích cuối của saga escalation chat.
///
/// <c>ChatEscalationReviewSagaStateMachine</c> (TicketService) publish
/// <see cref="ChatEscalatedToAdminEvent"/> khi Manager không ACK trong 30 phút, nhưng KHÔNG service
/// nào consume → saga chạy đúng, timeout đúng, publish đúng, rồi Admin không bao giờ được báo
/// (reviewnotification.md §3.3).
///
/// Ghi InApp + Push + Email cho toàn bộ Admin đang hoạt động.
/// </summary>
public class ChatEscalatedToAdminConsumer : IConsumer<ChatEscalatedToAdminEvent>
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly IRecipientResolver _recipientResolver;
    private readonly ICacheService _cache;
    private readonly ILogger<ChatEscalatedToAdminConsumer> _logger;

    public ChatEscalatedToAdminConsumer(
        INotificationUnitOfWork unitOfWork,
        IRecipientResolver recipientResolver,
        ICacheService cache,
        ILogger<ChatEscalatedToAdminConsumer> logger)
    {
        _unitOfWork = unitOfWork;
        _recipientResolver = recipientResolver;
        _cache = cache;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ChatEscalatedToAdminEvent> context)
    {
        // GH-765 — chỗ giữ có hạn ngắn, chỉ nâng lên cửa sổ 30 phút SAU KHI ghi xong.
        // Bản cũ chiếm key 30 phút ngay từ đầu, nên một lỗi DB/resolver ở lần đầu là mọi lần
        // gửi lại trong 30 phút đều bị coi là trùng ⇒ notification biến mất hẳn.
        await NotificationDebounce.ProcessOnceAsync(_cache, context, "ChatEscalatedToAdmin", _logger, async () =>
        {
            var evt = context.Message;

            var recipientIds = await _recipientResolver.GetActiveByRoleAsync(context.CancellationToken, "Admin");
            if (recipientIds.Count == 0)
            {
                _logger.LogWarning(
                    "No Admin recipient resolved for ChatEscalatedToAdmin ticket={TicketId} — skip.", evt.TicketId);
                return;
            }

            var title = $"🚨 Chat escalated to Admin — ticket {evt.TicketCode}";
            var body = $"The Manager did not respond to the chat review request on ticket {evt.TicketCode} within 30 minutes. " +
                       "Admin action is required.";
            var payload = JsonSerializer.Serialize(new
            {
                chatId = evt.ChatId,
                ticketId = evt.TicketId,
                ticketCode = evt.TicketCode,
                managerUserId = evt.ManagerUserId,
                screen = "TicketDetail"
            });

            await NotificationWriter.WriteAsync(
                _unitOfWork, recipientIds, NotificationTypeEnum.ChatEscalatedToAdmin, NotificationWriter.InAppPushEmail,
                title, body, payload, "Ticket", evt.TicketId, context.CancellationToken);
        });
    }
}
