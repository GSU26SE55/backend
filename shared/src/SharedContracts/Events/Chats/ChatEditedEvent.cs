using SharedContracts.Events.Root;

namespace SharedContracts.Events.Chats;

/// <summary>
/// Publish khi chat được chỉnh sửa bởi author hoặc admin.
/// Subscribers: NotificationService (notify thread, optional).
/// </summary>
public record ChatEditedEvent(
    Guid ChatId,
    Guid TicketId,
    Guid ActorUserId,
    int ActorRole,              // ActorRoleEnum value
    string OldBody,
    string NewBody,
    string EditReason
) : IntegrationEvent;
