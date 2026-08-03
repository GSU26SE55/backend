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
        var messageId = context.MessageId ?? Guid.Empty;
        if (messageId != Guid.Empty && !await NotificationDebounce.TryBeginByMessageAsync(_cache, messageId, context.CancellationToken))
        {
            _logger.LogInformation("Debounce: skip duplicate ChatEscalatedToAdmin message={MessageId}", messageId);
            return;
        }

        var evt = context.Message;

        var recipientIds = await _recipientResolver.GetActiveByRoleAsync(context.CancellationToken, "Admin");
        if (recipientIds.Count == 0)
        {
            _logger.LogWarning(
                "No Admin recipient resolved for ChatEscalatedToAdmin ticket={TicketId} — skip.", evt.TicketId);
            return;
        }

        var title = $"🚨 Escalation chat lên Admin — ticket {evt.TicketCode}";
        var body = $"Manager không phản hồi yêu cầu review chat trên ticket {evt.TicketCode} trong 30 phút. " +
                   "Cần Admin vào xử lý.";
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
    }
}
