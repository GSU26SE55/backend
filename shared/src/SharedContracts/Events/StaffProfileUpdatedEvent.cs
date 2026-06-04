using SharedContracts.Events.Root;

namespace SharedContracts.Events;

public record StaffProfileUpdatedEvent(
    Guid AccountId,
    string? EmployeeCode,
    int MaxConcurrentTickets,
    bool IsAvailable
) : IntegrationEvent;
