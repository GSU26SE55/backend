using SharedContracts.Events.Root;

namespace SharedContracts.Events.Chats;

/// <summary>
/// Publish khi có user được mention trong nội dung chat (cá nhân hoặc nhóm @team/@role).
/// Subscribers: NotificationService (push notify mentioned user).
/// </summary>
public record ChatMentionedEvent(
    Guid ChatId,
    Guid TicketId,
    Guid MentionedUserId,
    int MentionedUserRole,      // ActorRoleEnum value
    string MentionedDisplayName,
    Guid ActorUserId,
    bool IsGroupMention
) : IntegrationEvent;
