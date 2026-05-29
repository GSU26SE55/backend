using SharedKernels.Domain;

namespace TicketService.Application.StateMachine;

public sealed class TransitionResult
{
    public bool IsAllowed { get; init; }
    public string? Reason { get; init; }

    public List<DomainEvent> RaisedEvents { get; init; } = new();
}
