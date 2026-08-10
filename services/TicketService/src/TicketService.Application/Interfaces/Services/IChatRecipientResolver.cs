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
    ///
    /// <para><b>Ứng viên</b> (cả hai trường hợp): chủ ticket + primary handler + supporter +
    /// participant còn hoạt động + mọi người đã từng nhắn trên ticket. PreviousPrimaryHandler
    /// không tính — đã bàn giao thì thôi.</para>
    ///
    /// <para><b>Công khai:</b> lấy hết ứng viên.</para>
    ///
    /// <para><b>Nội bộ:</b> lọc bằng ĐÚNG luật đọc đang chạy —
    /// <c>TicketQueryHelper.CanViewInternalChats(roles, participantCanViewInternal)</c>, tức
    /// Admin/Manager/Staff theo vai trò, cộng thêm participant bất kỳ đã được cấp cờ
    /// <c>CanViewInternal</c> (#522). Không tự chế luật riêng ở đây: danh sách "được báo" phải
    /// trùng khít danh sách "đọc được", nếu không thì hoặc bỏ sót người, hoặc hé nội dung nội bộ
    /// cho người không có quyền.</para>
    /// </summary>
    Task<List<Guid>> ResolveAsync(
        Guid ticketId,
        Guid customerId,
        Guid authorUserId,
        bool isInternal,
        CancellationToken cancellationToken = default);
}
