using SharedContracts.Events.Root;

namespace SharedContracts.Events.Chats;

/// <summary>
/// Publish khi Manager ACK escalation review — transition saga Pending → Reviewed.
/// CorrelationId = ChatId.
/// </summary>
public record ChatEscalationReviewAckedEvent(
    Guid CorrelationId,
    Guid ManagerUserId,
    DateTime AckedAt
) : IntegrationEvent;
