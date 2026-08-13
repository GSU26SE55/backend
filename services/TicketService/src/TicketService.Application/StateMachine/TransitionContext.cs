using TicketService.Domain.Enums;

namespace TicketService.Application.StateMachine;

public class TransitionContext
{
    public ActorRoleEnum ActorRole { get; init; }
    public Guid ActorUserId { get; init; }
    public string ActorDisplayName { get; init; } = string.Empty;
    public Dictionary<string, object?> Payload { get; init; } = new();
}
