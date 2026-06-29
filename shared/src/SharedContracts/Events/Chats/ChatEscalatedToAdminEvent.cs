using SharedContracts.Events.Root;

namespace SharedContracts.Events.Chats;

/// <summary>
/// Publish khi saga ChatEscalationReview timeout (30 phút Manager không ACK).
/// Consumers: NotificationService → gửi push/email cho Admin.
/// </summary>
public record ChatEscalatedToAdminEvent(
    Guid ChatId,
    Guid TicketId,
    string TicketCode,
    Guid ManagerUserId
) : IntegrationEvent;
