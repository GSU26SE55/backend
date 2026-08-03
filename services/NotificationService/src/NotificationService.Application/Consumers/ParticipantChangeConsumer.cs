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
/// Consumer cho <see cref="ParticipantAddedEvent"/> + <see cref="ParticipantRemovedEvent"/> +
/// <see cref="ParticipantRoleChangedEvent"/>: welcome notify khi được thêm vào ticket, farewell
/// notify khi bị xóa, notify khi role/type bị đổi (#544).
///
/// Sprint 6.3 NOTI3-01 (#701) — cả 3 luồng nay ghi SONG SONG <c>InApp</c> + <c>Push</c>.
/// Trước đó chỉ ghi <c>Push</c>; sau khi feed in-app lọc theo <c>Channel = InApp</c> thì 3 loại
/// thông báo này sẽ biến mất hoàn toàn khỏi danh sách của user (rủi ro R-40 trong spec).
/// </summary>
public class ParticipantChangeConsumer :
    IConsumer<ParticipantAddedEvent>,
    IConsumer<ParticipantRemovedEvent>,
    IConsumer<ParticipantRoleChangedEvent>
{
    /// <summary>Kênh cho mọi notification participant: có mặt trong feed + đẩy push.</summary>
    private static readonly NotificationChannelEnum[] Channels =
    [
        NotificationChannelEnum.InApp,
        NotificationChannelEnum.Push
    ];

    private readonly IMediator _mediator;
    private readonly IInboxStore _inboxStore;
    private readonly ILogger<ParticipantChangeConsumer> _logger;

    public ParticipantChangeConsumer(IMediator mediator, IInboxStore inboxStore, ILogger<ParticipantChangeConsumer> logger)
    {
        _mediator = mediator;
        _inboxStore = inboxStore;
        _logger = logger;
    }

    public Task Consume(ConsumeContext<ParticipantAddedEvent> context) =>
        context.ProcessOnceAsync(_inboxStore, nameof(ParticipantChangeConsumer), () =>
        {
            var evt = context.Message;
            return WriteAsync(
                context,
                evt.ParticipantUserId,
                NotificationTypeEnum.ParticipantAdded,
                "Bạn đã được thêm vào ticket",
                "Bạn vừa được thêm làm participant của 1 ticket.",
                JsonSerializer.Serialize(new { ticketId = evt.TicketId }),
                evt.TicketId);
        });

    public Task Consume(ConsumeContext<ParticipantRemovedEvent> context) =>
        context.ProcessOnceAsync(_inboxStore, nameof(ParticipantChangeConsumer), () =>
        {
            var evt = context.Message;
            return WriteAsync(
                context,
                evt.ParticipantUserId,
                NotificationTypeEnum.ParticipantRemoved,
                "Bạn đã bị xóa khỏi ticket",
                "Bạn không còn là participant của 1 ticket.",
                JsonSerializer.Serialize(new { ticketId = evt.TicketId }),
                evt.TicketId);
        });

    public Task Consume(ConsumeContext<ParticipantRoleChangedEvent> context) =>
        context.ProcessOnceAsync(_inboxStore, nameof(ParticipantChangeConsumer), () =>
        {
            var evt = context.Message;
            return WriteAsync(
                context,
                evt.ParticipantUserId,
                NotificationTypeEnum.ParticipantRoleChanged,
                "Vai trò của bạn trên ticket đã thay đổi",
                "Vai trò participant của bạn trên 1 ticket vừa được cập nhật.",
                JsonSerializer.Serialize(new
                {
                    ticketId = evt.TicketId,
                    oldType = evt.OldType,
                    newType = evt.NewType,
                }),
                evt.TicketId);
        });

    private async Task WriteAsync<T>(
        ConsumeContext<T> context,
        Guid userId,
        NotificationTypeEnum type,
        string title,
        string body,
        string payloadJson,
        Guid ticketId)
        where T : class
    {
        foreach (var channel in Channels)
        {
            var result = await _mediator.Send(new CreateNotificationCommand
            {
                UserId = userId,
                Type = type,
                Channel = channel,
                Title = title,
                Body = body,
                PayloadJson = payloadJson,
                EntityType = "Ticket",
                EntityId = ticketId
            }, context.CancellationToken);

            if (!result.IsSuccess)
            {
                _logger.LogWarning(
                    "Failed to create {Type} {Channel} notification for TicketId={TicketId}, UserId={UserId}: {Message}",
                    type, channel, ticketId, userId, result.Message);
            }
        }
    }
}
