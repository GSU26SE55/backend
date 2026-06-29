using SharedContracts.Events.Root;

namespace SharedContracts.Events.Chats;

/// <summary>
/// Publish khi role/type của participant được cập nhật.
/// Subscribers: NotificationService (notify role change).
/// </summary>
public record ParticipantRoleChangedEvent(
    Guid TicketId,
    Guid ParticipantUserId,
    int OldType,                // ParticipantTypeEnum value
    int NewType,                // ParticipantTypeEnum value
    Guid ActorUserId
) : IntegrationEvent;
