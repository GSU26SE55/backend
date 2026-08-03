using MediatR;
using NotificationService.Application.DTOs.Response.Notification;
using NotificationService.Domain.Enums;
using SharedContracts.Common.Requests;

namespace NotificationService.Application.CQRS.Query.NotificationGroup;

/// <summary>
/// Sprint 6.4 NOTI4-02 — danh sách nhóm có phân trang.
///
/// <para>Kế thừa <see cref="PaginationRequest"/> để dùng chung quy tắc kẹp của toàn hệ thống
/// (<c>pageNumber &lt;= 0</c> → 1; <c>pageSize &lt;= 0</c> → 10; <c>&gt; 100</c> → 100).</para>
/// </summary>
public class NotificationGroupGetListQuery : PaginationRequest, IRequest<NotificationGroupListResponse>
{
    /// <summary>Lọc theo loại nhóm. Nhận cả tên enum (<c>Static</c>) lẫn số (<c>1</c>).</summary>
    public NotificationGroupKindEnum? Kind { get; set; }

    /// <summary>Tìm theo tên nhóm, không phân biệt hoa-thường. Khớp một phần.</summary>
    public string? Search { get; set; }
}

/// <summary>Sprint 6.4 NOTI4-02 — chi tiết một nhóm, kèm số người nhận thực tế.</summary>
public class NotificationGroupGetByIdQuery : IRequest<NotificationGroupResponse>
{
    public Guid Id { get; set; }
}

/// <summary>
/// Sprint 6.4 NOTI4-03 — thành viên của một nhóm, có phân trang.
///
/// <para>Với nhóm <c>Role</c>, kết quả được <b>suy ra</b> từ read-model tài khoản chứ không đọc bảng
/// thành viên — nên <c>AddedAt</c> luôn <c>null</c>.</para>
/// </summary>
public class NotificationGroupGetMembersQuery : PaginationRequest, IRequest<NotificationGroupMemberListResponse>
{
    public Guid GroupId { get; set; }

    /// <summary>Tìm theo tên hoặc email, không phân biệt hoa-thường.</summary>
    public string? Search { get; set; }

    /// <summary>
    /// <c>true</c> ⇒ chỉ trả người đang nhận được thông báo. Mặc định <c>false</c> để admin thấy cả
    /// người đã nghỉ còn sót lại trong nhóm mà dọn.
    /// </summary>
    public bool? ActiveOnly { get; set; }
}
