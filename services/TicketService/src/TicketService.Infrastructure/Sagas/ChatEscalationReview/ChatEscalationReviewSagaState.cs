using MassTransit;

namespace TicketService.Infrastructure.Sagas.ChatEscalationReview;

/// <summary>
/// Saga state: trigger khi Manager được mention trên ticket P1 Critical.
/// CorrelationId = ChatId (1 saga per chat message, không trùng).
/// States: Initial → Pending → (Reviewed | Escalated).
/// #566.
/// </summary>
public class ChatEscalationReviewSagaState : SagaStateMachineInstance, ISagaVersion
{
    public Guid CorrelationId { get; set; }
    public string CurrentState { get; set; } = null!;
    public int Version { get; set; }

    public Guid TicketId { get; set; }
    public string TicketCode { get; set; } = string.Empty;
    public Guid ManagerUserId { get; set; }
    public string EscalationReason { get; set; } = string.Empty;

    public DateTime StartedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }

    public Guid? PendingTimeoutTokenId { get; set; }
}
