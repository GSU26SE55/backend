namespace TicketService.Application.Interfaces.Services;

/// <summary>
/// Gom danh sách người cần nhận thông báo cho một chat mới.
/// Chỉ TicketService biết assignment + participant nên phải tính ở đây rồi gắn vào
/// <c>ChatCreatedEvent</c>; NotificationService không có dữ liệu để tự suy ra.
/// </summary>
public interface IChatRecipientResolver
{
    /// <summary>
    /// Trả về danh sách UserId đã loại tác giả, đã distinct.
    /// Công khai: Customer + primary handler + supporter + participant còn hoạt động +
    /// mọi người đã từng nhắn trên ticket.
    /// Nội bộ: cùng bộ đó nhưng bỏ hết Customer và participant không có quyền xem internal.
    /// </summary>
    Task<List<Guid>> ResolveAsync(
        Guid ticketId,
        Guid customerId,
        Guid authorUserId,
        bool isInternal,
        CancellationToken cancellationToken = default);
}
