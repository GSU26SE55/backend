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
/// </summary>
public class ParticipantChangeConsumer :
    IConsumer<ParticipantAddedEvent>,
    IConsumer<ParticipantRemovedEvent>,
    IConsumer<ParticipantRoleChangedEvent>
{
    private readonly IMediator _mediator;
    private readonly IInboxStore _inboxStore;
    private readonly ILogger<ParticipantChangeConsumer> _logger;

    public ParticipantChangeConsumer(IMediator mediator, IInboxStore inboxStore, ILogger<ParticipantChangeConsumer> logger)
    {
        _mediator = mediator;
        _inboxStore = inboxStore;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ParticipantAddedEvent> context)
    {
        await context.ProcessOnceAsync(_inboxStore, nameof(ParticipantChangeConsumer), async () =>
        {
            var evt = context.Message;

            var cmd = new CreateNotificationCommand
            {
                UserId = evt.ParticipantUserId,
                Type = NotificationTypeEnum.ParticipantAdded,
                Channel = NotificationChannelEnum.Push,
                Title = "Bạn đã được thêm vào ticket",
                Body = "Bạn vừa được thêm làm participant của 1 ticket.",
                PayloadJson = $"{{\"ticketId\":\"{evt.TicketId}\"}}",
                EntityType = "Ticket",
                EntityId = evt.TicketId
            };

            var result = await _mediator.Send(cmd, context.CancellationToken);
            if (!result.IsSuccess)
            {
                _logger.LogWarning(
                    "Failed to create ParticipantAdded notification for TicketId={TicketId}, UserId={UserId}: {Message}",
                    evt.TicketId, evt.ParticipantUserId, result.Message);
            }
        });
    }

    public async Task Consume(ConsumeContext<ParticipantRemovedEvent> context)
    {
        await context.ProcessOnceAsync(_inboxStore, nameof(ParticipantChangeConsumer), async () =>
        {
            var evt = context.Message;

            var cmd = new CreateNotificationCommand
            {
                UserId = evt.ParticipantUserId,
                Type = NotificationTypeEnum.ParticipantRemoved,
                Channel = NotificationChannelEnum.Push,
                Title = "Bạn đã bị xóa khỏi ticket",
                Body = "Bạn không còn là participant của 1 ticket.",
                PayloadJson = $"{{\"ticketId\":\"{evt.TicketId}\"}}",
                EntityType = "Ticket",
                EntityId = evt.TicketId
            };

            var result = await _mediator.Send(cmd, context.CancellationToken);
            if (!result.IsSuccess)
            {
                _logger.LogWarning(
                    "Failed to create ParticipantRemoved notification for TicketId={TicketId}, UserId={UserId}: {Message}",
                    evt.TicketId, evt.ParticipantUserId, result.Message);
            }
        });
    }

    public async Task Consume(ConsumeContext<ParticipantRoleChangedEvent> context)
    {
        await context.ProcessOnceAsync(_inboxStore, nameof(ParticipantChangeConsumer), async () =>
        {
            var evt = context.Message;

            var cmd = new CreateNotificationCommand
            {
                UserId = evt.ParticipantUserId,
                Type = NotificationTypeEnum.ParticipantRoleChanged,
                Channel = NotificationChannelEnum.Push,
                Title = "Vai trò của bạn trên ticket đã thay đổi",
                Body = "Vai trò participant của bạn trên 1 ticket vừa được cập nhật.",
                PayloadJson = $"{{\"ticketId\":\"{evt.TicketId}\",\"oldType\":{evt.OldType},\"newType\":{evt.NewType}}}",
                EntityType = "Ticket",
                EntityId = evt.TicketId
            };

            var result = await _mediator.Send(cmd, context.CancellationToken);
            if (!result.IsSuccess)
            {
                _logger.LogWarning(
                    "Failed to create ParticipantRoleChanged notification for TicketId={TicketId}, UserId={UserId}: {Message}",
                    evt.TicketId, evt.ParticipantUserId, result.Message);
            }
        });
    }
}
