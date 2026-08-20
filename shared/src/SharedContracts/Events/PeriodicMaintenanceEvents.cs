using SharedContracts.Events.Root;

namespace SharedContracts.Events;

public enum PeriodicMaintenanceReminderStage
{
    CustomerFirstReminder = 1,
    CustomerSecondReminder = 2,
    ManagerEscalation = 3
}

public record PeriodicMaintenanceReminderDueEvent(
    Guid TicketId,
    string Code,
    Guid BatteryAssetId,
    Guid CustomerId,
    DateTime MaintenanceDueAtUtc,
    DateTime ScheduleDeadlineAtUtc,
    PeriodicMaintenanceReminderStage Stage,
    bool IsOverdue
) : IntegrationEvent;

public record PeriodicMaintenanceScheduleChangedEvent(
    Guid TicketId,
    string Code,
    Guid BatteryAssetId,
    Guid CustomerId,
    DateTime? PreviousScheduledStartAtUtc,
    DateTime ScheduledStartAtUtc,
    int ScheduleVersion,
    string ChangedByRole,
    Guid ChangedByUserId,
    string? Reason,
    DateTime MaintenanceDueAtUtc,
    bool IsOverdue
) : IntegrationEvent;
