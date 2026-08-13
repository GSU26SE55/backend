using MassTransit;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Domain.Enums;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace NotificationService.Application.Consumers;

public sealed class TicketWorkStartedConsumer : IConsumer<TicketWorkStartedEvent>
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly ICacheService _cache;
    private readonly ILogger<TicketWorkStartedConsumer> _logger;

    public TicketWorkStartedConsumer(
        INotificationUnitOfWork unitOfWork,
        ICacheService cache,
        ILogger<TicketWorkStartedConsumer> logger)
    {
        _unitOfWork = unitOfWork;
        _cache = cache;
        _logger = logger;
    }

    public Task Consume(ConsumeContext<TicketWorkStartedEvent> context) =>
        NotificationDebounce.ProcessOnceAsync(_cache, context, nameof(TicketWorkStartedEvent), _logger, async () =>
        {
            var evt = context.Message;
            var recipients = new[] { evt.CustomerId, evt.PrimaryHandlerStaffId }
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();

            if (recipients.Count == 0)
                return;

            // Transport delivery is at-least-once and can be reordered. Persist the assignment
            // receipt first; the later TicketAssigned delivery uses the same deterministic keys.
            await TicketSchedulingNotificationWriter.WriteAssignmentAsync(
                _unitOfWork, evt.TicketId, evt.Code, evt.CustomerId,
                evt.PrimaryHandlerStaffId, evt.Priority, evt.ScheduledStartAtUtc,
                evt.ScheduleVersion, workStartsImmediately: true, context.CancellationToken);

            await TicketSchedulingNotificationWriter.WriteWorkStartedAsync(
                _unitOfWork, evt.TicketId, evt.Code, evt.CustomerId,
                evt.PrimaryHandlerStaffId, evt.StartedAtUtc, evt.ScheduleVersion,
                evt.ActivationReason, context.CancellationToken);
        });
}
