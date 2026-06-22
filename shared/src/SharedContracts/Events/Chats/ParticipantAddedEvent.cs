using SharedContracts.Events.Root;

namespace SharedContracts.Events.Chats;

/// <summary>
/// Publish khi participant được thêm vào ticket.
/// Subscribers: NotificationService (welcome notify).
/// </summary>
public record ParticipantAddedEvent(
    Guid TicketId,
    Guid ParticipantUserId,
    int ParticipantRole,        // ActorRoleEnum value
    int ParticipantType,        // ParticipantTypeEnum value
    Guid AddedByUserId
) : IntegrationEvent;
