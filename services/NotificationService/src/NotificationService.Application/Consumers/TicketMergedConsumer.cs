using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using NotificationService.Application.CQRS.Command.Notification;
using NotificationService.Domain.Enums;
using SharedContracts.Events;
using SharedInfrastructure.Idempotency;

namespace NotificationService.Application.Consumers;

/// <summary>Creates one in-app notification for the customer whose ticket was merged.</summary>
public sealed class TicketMergedConsumer : IConsumer<TicketMergedEvent>
{
    private readonly IMediator _mediator;
    private readonly IInboxStore _inboxStore;
    private readonly ILogger<TicketMergedConsumer> _logger;

    public TicketMergedConsumer(
        IMediator mediator,
        IInboxStore inboxStore,
        ILogger<TicketMergedConsumer> logger)
    {
        _mediator = mediator;
        _inboxStore = inboxStore;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TicketMergedEvent> context)
    {
        await context.ProcessOnceAsync(_inboxStore, nameof(TicketMergedConsumer), async () =>
        {
            var evt = context.Message;
            if (evt.SourceCustomerId == Guid.Empty)
            {
                _logger.LogWarning("TicketMerged event for source ticket {SourceTicketId} has no customer; notification skipped.", evt.SourceTicketId);
                return;
            }

            var response = await _mediator.Send(new CreateNotificationCommand
            {
                UserId = evt.SourceCustomerId,
                Type = NotificationTypeEnum.TicketMerged,
                Channel = NotificationChannelEnum.InApp,
                Title = $"Ticket {evt.SourceTicketCode} đã được gộp",
                Body = $"Ticket {evt.SourceTicketCode} đã được gộp vào ticket {evt.MasterTicketCode}.",
                PayloadJson = $"{{\"sourceTicketId\":\"{evt.SourceTicketId}\",\"masterTicketId\":\"{evt.MasterTicketId}\"}}",
                EntityType = "Ticket",
                EntityId = evt.MasterTicketId
            }, context.CancellationToken);

            if (!response.IsSuccess)
            {
                _logger.LogWarning("TicketMerged notification failed for source ticket {SourceTicketId}: {Message}", evt.SourceTicketId, response.Message);
            }
        });
    }
}
