using SharedContracts.Events.Root;

namespace SharedContracts.Events.Chats;

/// <summary>
/// Publish khi Manager được mention trên ticket P1 Critical — trigger escalation review saga.
/// Subscribers: TicketService Saga (ChatEscalationReviewSaga).
/// </summary>
public record ChatEscalationReviewRequestedEvent(
    Guid ChatId,
    Guid TicketId,
    Guid ManagerUserId,
    string EscalationReason
) : IntegrationEvent;
