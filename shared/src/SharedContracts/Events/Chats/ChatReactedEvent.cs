using SharedContracts.Events.Root;

namespace SharedContracts.Events.Chats;

/// <summary>
/// Publish khi user thêm hoặc bỏ reaction vào chat.
/// Subscribers: NotificationService (notify author of chat).
/// </summary>
public record ChatReactedEvent(
    Guid ChatId,
    Guid TicketId,
    Guid ActorUserId,
    int ActorRole,              // ActorRoleEnum value
    int ReactionType,           // ReactionTypeEnum value
    bool IsRemoved
) : IntegrationEvent;
