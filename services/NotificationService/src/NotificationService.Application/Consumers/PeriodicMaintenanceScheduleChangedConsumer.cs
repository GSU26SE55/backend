using System.Text.Json;
using MassTransit;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Domain.Enums;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace NotificationService.Application.Consumers;

public sealed class PeriodicMaintenanceScheduleChangedConsumer
    : IConsumer<PeriodicMaintenanceScheduleChangedEvent>
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly IRecipientResolver _recipientResolver;
    private readonly ICacheService _cache;
    private readonly ILogger<PeriodicMaintenanceScheduleChangedConsumer> _logger;

    public PeriodicMaintenanceScheduleChangedConsumer(
        INotificationUnitOfWork unitOfWork,
        IRecipientResolver recipientResolver,
        ICacheService cache,
        ILogger<PeriodicMaintenanceScheduleChangedConsumer> logger)
    {
        _unitOfWork = unitOfWork;
        _recipientResolver = recipientResolver;
        _cache = cache;
        _logger = logger;
    }

    public Task Consume(ConsumeContext<PeriodicMaintenanceScheduleChangedEvent> context) =>
        NotificationDebounce.ProcessOnceAsync(
            _cache,
            context,
            nameof(PeriodicMaintenanceScheduleChangedEvent),
            _logger,
            async () =>
            {
                var evt = context.Message;
                var managers = await _recipientResolver.GetActiveByRoleAsync(
                    context.CancellationToken,
                    "Manager");
                var recipients = string.Equals(
                    evt.ChangedByRole,
                    "Customer",
                    StringComparison.OrdinalIgnoreCase)
                    ? managers
                    : managers.Append(evt.CustomerId).ToList();

                var payload = JsonSerializer.Serialize(new
                {
                    ticketId = evt.TicketId,
                    code = evt.Code,
                    batteryAssetId = evt.BatteryAssetId,
                    previousScheduledStartAtUtc = evt.PreviousScheduledStartAtUtc,
                    scheduledStartAtUtc = evt.ScheduledStartAtUtc,
                    scheduleVersion = evt.ScheduleVersion,
                    changedByRole = evt.ChangedByRole,
                    changedByUserId = evt.ChangedByUserId,
                    reason = evt.Reason,
                    maintenanceDueAtUtc = evt.MaintenanceDueAtUtc,
                    isOverdue = evt.IsOverdue,
                    screen = "TicketDetail"
                });

                await NotificationWriter.WriteIdempotentAsync(
                    _unitOfWork,
                    recipients.Where(id => id != Guid.Empty).Distinct().ToList(),
                    NotificationTypeEnum.PeriodicMaintenanceScheduleChanged,
                    NotificationWriter.InAppPush,
                    $"Maintenance schedule changed for {evt.Code}",
                    $"The maintenance visit is scheduled for {evt.ScheduledStartAtUtc:O}.",
                    payload,
                    "Ticket",
                    evt.TicketId,
                    $"periodic-maintenance-schedule:{evt.ScheduleVersion}",
                    context.CancellationToken);
            });
}
