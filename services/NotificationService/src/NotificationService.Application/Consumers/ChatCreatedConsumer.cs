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
/// Consumer cho <see cref="ChatCreatedEvent"/>: notify MỌI người liên quan tới ticket, trừ tác giả.
///
/// Danh sách người nhận do TicketService tính sẵn (<c>RecipientUserIds</c>) vì chỉ bên đó mới biết
/// assignment + participant. Chat công khai gửi cho Customer + primary handler + supporter +
/// participant; chat nội bộ chỉ gửi cho phía vận hành có quyền xem internal — Customer không bao giờ
/// nằm trong danh sách, việc lọc đã làm ở publisher.
///
/// Trước đây consumer chỉ notify ĐÚNG MỘT người (Customer hoặc primary handler) và bỏ qua hoàn toàn
/// chat nội bộ, nên supporter/participant không bao giờ biết có tin nhắn mới.
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
            var recipients = ResolveRecipients(evt);

            if (recipients.Count == 0)
            {
                _logger.LogWarning(
                    "ChatCreated ChatId={ChatId} TicketId={TicketId} IsInternal={IsInternal} " +
                    "không có người nhận nào — bỏ qua.",
                    evt.ChatId, evt.TicketId, evt.IsInternal);
                return;
            }

            var title = evt.IsInternal ? "Ghi chú nội bộ mới trên ticket" : "Tin nhắn mới trên ticket";
            var payloadJson = JsonSerializer.Serialize(new
            {
                chatId = evt.ChatId,
                ticketId = evt.TicketId,
                senderName = evt.AuthorDisplayName,
                isInternal = evt.IsInternal
            });

            foreach (var recipientId in recipients)
            {
                foreach (var channel in new[] { NotificationChannelEnum.InApp, NotificationChannelEnum.Push })
                {
                    var cmd = new CreateNotificationCommand
                    {
                        UserId = recipientId,
                        Type = NotificationTypeEnum.ChatCreated,
                        Channel = channel,
                        Title = title,
                        Body = $"{evt.AuthorDisplayName}: {Truncate(evt.Body)}",
                        PayloadJson = payloadJson,
                        EntityType = "Chat",
                        EntityId = evt.ChatId
                    };

                    var result = await _mediator.Send(cmd, context.CancellationToken);
                    if (!result.IsSuccess)
                    {
                        _logger.LogWarning(
                            "Failed to create ChatCreated {Channel} notification for ChatId={ChatId} UserId={UserId}: {Message}",
                            channel, evt.ChatId, recipientId, result.Message);
                    }
                }
            }
        });
    }

    /// <summary>
    /// Ưu tiên danh sách publisher đã tính. Fallback chỉ phục vụ message publish từ bản cũ còn
    /// tồn trong queue — bản cũ không mang danh sách nên đành suy ra một người như trước.
    /// </summary>
    private static List<Guid> ResolveRecipients(ChatCreatedEvent evt)
    {
        if (evt.RecipientUserIds is { Count: > 0 })
        {
            return evt.RecipientUserIds
                .Where(id => id != Guid.Empty && id != evt.AuthorUserId)
                .Distinct()
                .ToList();
        }

        if (evt.IsInternal)
            return new List<Guid>();

        var isStaffAuthor = evt.AuthorRole != (int)ActorRoleEnumMirror.Customer;
        var legacyRecipient = isStaffAuthor ? evt.CustomerId : evt.AssignedStaffId;

        return legacyRecipient is null || legacyRecipient == Guid.Empty
            ? new List<Guid>()
            : new List<Guid> { legacyRecipient.Value };
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
