using System.Text.Json;
using MassTransit;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Domain.Enums;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace NotificationService.Application.Consumers;

public sealed class PeriodicMaintenanceReminderDueConsumer
    : IConsumer<PeriodicMaintenanceReminderDueEvent>
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly IRecipientResolver _recipientResolver;
    private readonly ICacheService _cache;
    private readonly ILogger<PeriodicMaintenanceReminderDueConsumer> _logger;

    public PeriodicMaintenanceReminderDueConsumer(
        INotificationUnitOfWork unitOfWork,
        IRecipientResolver recipientResolver,
        ICacheService cache,
        ILogger<PeriodicMaintenanceReminderDueConsumer> logger)
    {
        _unitOfWork = unitOfWork;
        _recipientResolver = recipientResolver;
        _cache = cache;
        _logger = logger;
    }

    public Task Consume(ConsumeContext<PeriodicMaintenanceReminderDueEvent> context) =>
        NotificationDebounce.ProcessOnceAsync(
            _cache,
            context,
            nameof(PeriodicMaintenanceReminderDueEvent),
            _logger,
            async () =>
            {
                var evt = context.Message;
                IReadOnlyCollection<Guid> recipients = evt.Stage ==
                    PeriodicMaintenanceReminderStage.ManagerEscalation
                    ? await _recipientResolver.GetActiveByRoleAsync(
                        context.CancellationToken,
                        "Manager")
                    : [evt.CustomerId];

                var payload = JsonSerializer.Serialize(new
                {
                    ticketId = evt.TicketId,
                    code = evt.Code,
                    batteryAssetId = evt.BatteryAssetId,
                    maintenanceDueAtUtc = evt.MaintenanceDueAtUtc,
                    scheduleDeadlineAtUtc = evt.ScheduleDeadlineAtUtc,
                    stage = evt.Stage.ToString(),
                    isOverdue = evt.IsOverdue,
                    screen = "TicketDetail"
                });

                await NotificationWriter.WriteIdempotentAsync(
                    _unitOfWork,
                    recipients.Where(id => id != Guid.Empty).Distinct().ToList(),
                    NotificationTypeEnum.PeriodicMaintenanceReminder,
                    NotificationWriter.InAppPush,
                    evt.Stage == PeriodicMaintenanceReminderStage.ManagerEscalation
                        ? $"Periodic maintenance needs scheduling for {evt.Code}"
                        : $"Schedule periodic maintenance for {evt.Code}",
                    evt.IsOverdue
                        ? "The maintenance due date has passed. Please arrange a visit."
                        : $"Maintenance is due at {evt.MaintenanceDueAtUtc:O}.",
                    payload,
                    "Ticket",
                    evt.TicketId,
                    $"periodic-maintenance-reminder:{(int)evt.Stage}",
                    context.CancellationToken);
            });
}
