using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using NotificationService.Application.CQRS.Command.Notification;
using NotificationService.Domain.Enums;
using SharedContracts.Events.Chats;
using SharedInfrastructure.Idempotency;

namespace NotificationService.Application.Consumers;

/// <summary>
/// Consumer cho <see cref="ChatReactedEvent"/>: notify author của chat khi có reaction mới.
/// Skip nếu reaction bị remove hoặc actor tự reaction vào chat của chính mình.
/// </summary>
public class ChatReactionConsumer : IConsumer<ChatReactedEvent>
{
    private readonly IMediator _mediator;
    private readonly IInboxStore _inboxStore;
    private readonly ILogger<ChatReactionConsumer> _logger;

    public ChatReactionConsumer(IMediator mediator, IInboxStore inboxStore, ILogger<ChatReactionConsumer> logger)
    {
        _mediator = mediator;
        _inboxStore = inboxStore;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ChatReactedEvent> context)
    {
        await context.ProcessOnceAsync(_inboxStore, nameof(ChatReactionConsumer), async () =>
        {
            var evt = context.Message;

            if (evt.IsRemoved || evt.ChatAuthorUserId == evt.ActorUserId || evt.ChatAuthorUserId == Guid.Empty)
                return;

            // Sprint 6.3 NOTI3-01 (#701) — ghi thêm row InApp: feed nay lọc Channel=InApp,
            // nếu chỉ có Push thì reaction biến mất khỏi danh sách thông báo (R-40).
            foreach (var channel in new[] { NotificationChannelEnum.InApp, NotificationChannelEnum.Push })
            {
                var cmd = new CreateNotificationCommand
                {
                    UserId = evt.ChatAuthorUserId,
                    Type = NotificationTypeEnum.ChatReacted,
                    Channel = channel,
                    Title = "Chat của bạn có reaction mới",
                    Body = "Một chat của bạn vừa nhận được reaction.",
                    PayloadJson = $"{{\"chatId\":\"{evt.ChatId}\",\"ticketId\":\"{evt.TicketId}\",\"reactionType\":{evt.ReactionType}}}",
                    EntityType = "Chat",
                    EntityId = evt.ChatId
                };

                var result = await _mediator.Send(cmd, context.CancellationToken);
                if (!result.IsSuccess)
                {
                    _logger.LogWarning(
                        "Failed to create ChatReacted {Channel} notification for ChatId={ChatId}: {Message}",
                        channel, evt.ChatId, result.Message);
                }
            }
        });
    }
}
