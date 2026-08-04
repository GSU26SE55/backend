using MediatR;
using NotificationService.Application.DTOs.Response.Notification;
using NotificationService.Domain.Enums;
using SharedContracts.Common.Requests;

namespace NotificationService.Application.CQRS.Query.NotificationTemplate;

/// <summary>
/// Danh sách template có phân trang, lọc theo type/channel. Bao gồm cả bản không active để thấy
/// lịch sử phiên bản của từng cặp (Type × Channel).
///
/// <para>Kế thừa <see cref="PaginationRequest"/> để dùng chung quy tắc kẹp của toàn hệ thống
/// (<c>pageNumber &lt;= 0</c> → 1; <c>pageSize &lt;= 0</c> → 10; <c>&gt; 100</c> → 100).</para>
/// </summary>
public class NotificationTemplateGetListQuery : PaginationRequest, IRequest<NotificationTemplateListResponse>
{
    /// <summary>Lọc theo loại notification. Nhận cả tên enum (<c>SlaBreached</c>) lẫn số (<c>8</c>).</summary>
    public NotificationTypeEnum? Type { get; set; }

    /// <summary>Lọc theo kênh. Nhận cả tên enum (<c>Email</c>) lẫn số (<c>2</c>).</summary>
    public NotificationChannelEnum? Channel { get; set; }

    /// <summary>
    /// <c>true</c> ⇒ chỉ trả bản đang dùng của mỗi cặp (ẩn lịch sử phiên bản).
    /// Mặc định <c>false</c> — màn hình quản trị cần thấy đủ để quay lui.
    /// </summary>
    public bool? ActiveOnly { get; set; }
}
