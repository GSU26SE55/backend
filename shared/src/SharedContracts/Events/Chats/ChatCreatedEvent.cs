using SharedContracts.Events.Root;

namespace SharedContracts.Events.Chats;

/// <summary>
/// Publish khi chat mới được tạo trên Ticket.
/// Subscribers: NotificationService (push notify Customer/Staff), TicketService (detect ngôn ngữ).
/// </summary>
/// <remarks>
/// <c>RecipientUserIds</c> là danh sách người cần nhận thông báo, đã loại tác giả và đã lọc theo
/// <c>IsInternal</c> (nội bộ = chỉ phía vận hành có quyền xem internal). Publisher tính sẵn vì chỉ
/// TicketService mới biết assignment + participant.
/// Null/rỗng chỉ xảy ra với message publish từ bản cũ còn tồn trong queue — consumer khi đó quay về
/// suy luận từ <c>CustomerId</c>/<c>AssignedStaffId</c>.
/// </remarks>
public record ChatCreatedEvent(
    Guid ChatId,
    Guid TicketId,
    Guid AuthorUserId,
    int AuthorRole,             // ActorRoleEnum value
    string AuthorDisplayName,
    string Body,
    bool IsInternal,
    List<Guid> AttachmentFileIds,
    Guid CustomerId,
    Guid? AssignedStaffId,
    List<Guid>? RecipientUserIds = null
) : IntegrationEvent;
