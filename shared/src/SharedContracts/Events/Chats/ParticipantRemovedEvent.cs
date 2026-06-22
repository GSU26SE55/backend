using SharedContracts.Events.Root;

namespace SharedContracts.Events.Chats;

/// <summary>
/// Publish khi participant bị xóa khỏi ticket.
/// Subscribers: NotificationService + SignalR (force disconnect, notify).
/// </summary>
public record ParticipantRemovedEvent(
    Guid TicketId,
    Guid ParticipantUserId,
    Guid RemovedByUserId,
    string RemoveReason
) : IntegrationEvent;
