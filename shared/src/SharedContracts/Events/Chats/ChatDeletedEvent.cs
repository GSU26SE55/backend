using SharedContracts.Events.Root;

namespace SharedContracts.Events.Chats;

/// <summary>
/// Publish khi chat bị soft-delete.
/// Subscribers: NotificationService + Audit (update FE state, sync downstream).
/// </summary>
public record ChatDeletedEvent(
    Guid ChatId,
    Guid TicketId,
    Guid ActorUserId,
    int ActorRole               // ActorRoleEnum value
) : IntegrationEvent;
