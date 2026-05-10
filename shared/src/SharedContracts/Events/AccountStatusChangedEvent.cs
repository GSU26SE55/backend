using SharedContracts.Events.Root;

namespace SharedContracts.Events;

public record AccountStatusChangedEvent(
    Guid AccountId,
    string Email,
    int OldStatus,
    int NewStatus,
    string? Reason
) : IntegrationEvent;
