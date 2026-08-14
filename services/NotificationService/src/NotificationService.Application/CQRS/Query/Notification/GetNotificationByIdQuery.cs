using MediatR;
using NotificationService.Application.DTOs.Response.Notification;

namespace NotificationService.Application.CQRS.Query.Notification;

/// <summary>
/// Lấy chi tiết 1 notification của user hiện tại — phục vụ màn hình hộp thư (chi tiết thông báo).
///
/// Danh sách chỉ trả bản rút gọn theo trang; muốn mở riêng một thông báo (deep link, F5 giữa trang,
/// hoặc noti đã trôi khỏi trang đầu) thì phải đọc được đúng bản ghi đó mà không cần dò phân trang.
///
/// <see cref="UserId"/> LUÔN set từ JWT claim ở controller, không nhận từ client — nếu để client
/// truyền thì bất kỳ ai cũng đọc được thông báo của người khác chỉ bằng cách đổi id.
/// </summary>
public class GetNotificationByIdQuery : IRequest<NotificationResponse>
{
    public Guid Id { get; set; }

    /// <summary>Set từ JWT claim trong controller.</summary>
    public Guid UserId { get; set; }
}
