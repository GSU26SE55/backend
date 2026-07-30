using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using NotificationService.Application.CQRS.Command.Notification;
using NotificationService.Domain.Enums;
using SharedContracts.Events.Chats;
using SharedInfrastructure.Idempotency;

namespace NotificationService.Application.Consumers;

/// <summary>
/// Consumer cho <see cref="ChatCreatedEvent"/>: notify Customer khi Staff post chat public,
/// notify Staff (assigned) khi Customer post chat. Skip hoàn toàn nếu IsInternal=true.
///
/// Sprint 6.2 NOTI-10 (#681) — ghi SONG SONG record <c>Channel=InApp</c> bên cạnh <c>Channel=Push</c>.
/// Trước đó chỉ ghi mỗi Push: sai ngữ nghĩa kênh (lịch sử in-app lại phụ thuộc record của kênh push)
/// và nếu về sau API list lọc theo channel thì user mất sạch lịch sử chat (reviewnotification.md §4.4).
/// </summary>
public class ChatCreatedConsumer : IConsumer<ChatCreatedEvent>
{
    private readonly IMediator _mediator;
    private readonly IInboxStore _inboxStore;
    private readonly ILogger<ChatCreatedConsumer> _logger;

    public ChatCreatedConsumer(IMediator mediator, IInboxStore inboxStore, ILogger<ChatCreatedConsumer> logger)
    {
        _mediator = mediator;
        _inboxStore = inboxStore;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ChatCreatedEvent> context)
    {
        await context.ProcessOnceAsync(_inboxStore, nameof(ChatCreatedConsumer), async () =>
        {
            var evt = context.Message;

            if (evt.IsInternal)
                return;

            var isStaffAuthor = evt.AuthorRole != (int)ActorRoleEnumMirror.Customer;
            var recipientId = isStaffAuthor ? evt.CustomerId : evt.AssignedStaffId;

            if (recipientId == null || recipientId == Guid.Empty)
                return;

            foreach (var channel in new[] { NotificationChannelEnum.InApp, NotificationChannelEnum.Push })
            {
                var cmd = new CreateNotificationCommand
                {
                    UserId = recipientId.Value,
                    Type = NotificationTypeEnum.ChatCreated,
                    Channel = channel,
                    Title = "Tin nhắn mới trên ticket",
                    Body = $"{evt.AuthorDisplayName}: {Truncate(evt.Body)}",
                    PayloadJson = $"{{\"chatId\":\"{evt.ChatId}\",\"ticketId\":\"{evt.TicketId}\"}}",
                    EntityType = "Chat",
                    EntityId = evt.ChatId
                };

                var result = await _mediator.Send(cmd, context.CancellationToken);
                if (!result.IsSuccess)
                {
                    _logger.LogWarning(
                        "Failed to create ChatCreated {Channel} notification for ChatId={ChatId}: {Message}",
                        channel, evt.ChatId, result.Message);
                }
            }
        });
    }

    private static string Truncate(string body) => body.Length > 100 ? body[..100] + "..." : body;

    /// <summary>
    /// ActorRoleEnum value mirror (Customer=4) — tránh phụ thuộc trực tiếp vào TicketService.Domain.
    /// </summary>
    private enum ActorRoleEnumMirror
    {
        Admin = 1,
        Manager = 2,
        Staff = 3,
        Customer = 4
    }
}
