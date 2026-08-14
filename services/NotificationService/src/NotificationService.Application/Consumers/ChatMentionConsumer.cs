using System.Text.Json;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using NotificationService.Application.CQRS.Command.Notification;
using NotificationService.Domain.Enums;
using SharedContracts.Events.Chats;
using SharedInfrastructure.Idempotency;

namespace NotificationService.Application.Consumers;

/// <summary>
/// Consumer cho <see cref="ChatMentionedEvent"/>: push + email cho user được mention trong chat.
/// </summary>
public class ChatMentionConsumer : IConsumer<ChatMentionedEvent>
{
    private readonly IMediator _mediator;
    private readonly IInboxStore _inboxStore;
    private readonly ILogger<ChatMentionConsumer> _logger;

    public ChatMentionConsumer(IMediator mediator, IInboxStore inboxStore, ILogger<ChatMentionConsumer> logger)
    {
        _mediator = mediator;
        _inboxStore = inboxStore;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ChatMentionedEvent> context)
    {
        await context.ProcessOnceAsync(_inboxStore, nameof(ChatMentionConsumer), async () =>
        {
            var evt = context.Message;

            var title = "You were mentioned in a chat";
            var body = $"{evt.MentionedDisplayName} was mentioned in the ticket.";
            // 03/08/2026 — dựng bằng JsonSerializer thay cho chuỗi nội suy. Payload hiện chỉ chứa
            // Guid/int/bool nên chuỗi nội suy chưa từng sinh JSON hỏng, nhưng nó đúng chỉ nhờ
            // may: người sau thêm một trường chữ (tên người, tiêu đề ticket) mà quên escape là
            // dấu nháy trong đó cắt đôi JSON, và payload hỏng thì mọi biến template của loại đó
            // im lặng render ra rỗng. 26/31 consumer khác đã dùng JsonSerializer.
            var payload = JsonSerializer.Serialize(new
            {
                chatId = evt.ChatId,
                ticketId = evt.TicketId,
                isGroupMention = evt.IsGroupMention,
            });

            // Sprint 6.3 NOTI3-01 (#701) — thêm InApp: feed in-app nay lọc theo Channel=InApp,
            // thiếu row này thì mention biến mất hoàn toàn khỏi danh sách thông báo (R-40).
            foreach (var channel in new[] { NotificationChannelEnum.InApp, NotificationChannelEnum.Push, NotificationChannelEnum.Email })
            {
                var cmd = new CreateNotificationCommand
                {
                    UserId = evt.MentionedUserId,
                    Type = NotificationTypeEnum.ChatMentioned,
                    Channel = channel,
                    Title = title,
                    Body = body,
                    PayloadJson = payload,
                    EntityType = "Chat",
                    EntityId = evt.ChatId
                };

                var result = await _mediator.Send(cmd, context.CancellationToken);
                if (!result.IsSuccess)
                {
                    _logger.LogWarning(
                        "Failed to create ChatMentioned notification for ChatId={ChatId}, UserId={UserId}: {Message}",
                        evt.ChatId, evt.MentionedUserId, result.Message);
                }
            }
        });
    }
}
