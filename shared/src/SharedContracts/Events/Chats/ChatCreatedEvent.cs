using SharedContracts.Events.Root;

namespace SharedContracts.Events.Chats;

/// <summary>
/// Publish khi chat mới được tạo trên Ticket.
/// Subscribers: NotificationService (push notify Customer/Staff).
/// </summary>
public record ChatCreatedEvent(
    Guid ChatId,
    Guid TicketId,
    Guid AuthorUserId,
    int AuthorRole,             // ActorRoleEnum value
    string AuthorDisplayName,
    string Body,
    bool IsInternal,
    List<Guid> AttachmentFileIds
) : IntegrationEvent;
