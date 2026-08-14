using System.Text.Json;
using MassTransit;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Domain.Enums;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace NotificationService.Application.Consumers;

public sealed class TicketScheduleChangedConsumer : IConsumer<TicketScheduleChangedEvent>
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly IRecipientResolver _recipientResolver;
    private readonly ICacheService _cache;
    private readonly ILogger<TicketScheduleChangedConsumer> _logger;

    public TicketScheduleChangedConsumer(
        INotificationUnitOfWork unitOfWork,
        IRecipientResolver recipientResolver,
        ICacheService cache,
        ILogger<TicketScheduleChangedConsumer> logger)
    {
        _unitOfWork = unitOfWork;
        _recipientResolver = recipientResolver;
        _cache = cache;
        _logger = logger;
    }

    public Task Consume(ConsumeContext<TicketScheduleChangedEvent> context) =>
        NotificationDebounce.ProcessOnceAsync(
            _cache,
            context,
            nameof(TicketScheduleChangedEvent),
            _logger,
            async () =>
            {
                var evt = context.Message;
                var managerIds = await _recipientResolver.GetActiveByRoleAsync(
                    context.CancellationToken, "Manager");
                var recipients = managerIds
                    .Append(evt.CustomerId)
                    .Append(evt.PrimaryHandlerStaffId)
                    .Where(id => id != Guid.Empty)
                    .Distinct()
                    .ToList();

                var payload = JsonSerializer.Serialize(new
                {
                    ticketId = evt.TicketId,
                    code = evt.Code,
                    previousScheduledStartAtUtc = evt.PreviousScheduledStartAtUtc,
                    scheduledStartAtUtc = evt.ScheduledStartAtUtc,
                    scheduleVersion = evt.ScheduleVersion,
                    screen = "TicketDetail"
                });

                await NotificationWriter.WriteIdempotentAsync(
                    _unitOfWork,
                    recipients,
                    NotificationTypeEnum.TicketScheduleChanged,
                        NotificationWriter.InAppPushEmail,
                        $"Schedule changed for ticket {evt.Code}",
                        $"The next work time is {evt.ScheduledStartAtUtc:O}.",
                    payload,
                    "Ticket",
                    evt.TicketId,
                    $"ticket-schedule-changed:{evt.ScheduleVersion}",
                    context.CancellationToken);
            });
}
