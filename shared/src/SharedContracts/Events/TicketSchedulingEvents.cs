using SharedContracts.Events.Root;

namespace SharedContracts.Events;

public record TicketScheduleChangedEvent(
    Guid TicketId,
    string Code,
    Guid CustomerId,
    Guid PrimaryHandlerStaffId,
    DateTime? PreviousScheduledStartAtUtc,
    DateTime ScheduledStartAtUtc,
    int ScheduleVersion
) : IntegrationEvent;

public record TicketWorkStartedEvent(
    Guid TicketId,
    string Code,
    Guid CustomerId,
    Guid PrimaryHandlerStaffId,
    DateTime StartedAtUtc,
    int ScheduleVersion,
    string ActivationReason,
    string Priority = "Unknown",
    DateTime? ScheduledStartAtUtc = null
) : IntegrationEvent;
