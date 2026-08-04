using NotificationService.Domain.Enums;

namespace NotificationService.Application.DTOs.Response.Notification;

/// <summary>
/// Sprint 6.4 NOTI4-02 — một nhóm người nhận trên màn hình quản trị.
/// <see cref="Kind"/> trả về dạng SỐ; client tự ánh xạ sang nhãn tiếng Việt (cùng quy ước với
/// <c>NotificationTemplateDto.Type</c>/<c>Channel</c>).
/// </summary>
public class NotificationGroupDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>1 = Static (thành viên tường minh) · 2 = Role (suy ra lúc gửi).</summary>
    public NotificationGroupKindEnum Kind { get; set; }

    /// <summary>Chỉ có giá trị khi <see cref="Kind"/> = Role.</summary>
    public string? RoleFilter { get; set; }

    /// <summary>Nhóm hệ thống — client phải ẩn nút sửa/xoá.</summary>
    public bool IsSystem { get; set; }

    /// <summary>
    /// Số người nhận thực tế của nhóm — đã lọc người không hoạt động và đã xoá.
    ///
    /// <para>Với nhóm <c>Static</c>: đếm thành viên còn sống có tài khoản đang hoạt động (một thành
    /// viên trỏ tới tài khoản đã nghỉ vẫn nằm trong bảng nhưng KHÔNG được tính, vì lúc gửi cũng sẽ
    /// bị loại). Với nhóm <c>Role</c>: đếm tài khoản đang hoạt động khớp role.</para>
    ///
    /// <para>Đây là con số người vận hành cần thấy trước khi bấm gửi — không phải số dòng trong
    /// bảng thành viên.</para>
    /// </summary>
    public int MemberCount { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Sprint 6.4 NOTI4-03 — một thành viên của nhóm, kèm thông tin tài khoản để hiển thị.</summary>
public class NotificationGroupMemberDto
{
    /// <summary>AccountId. Với nhóm <c>Role</c> đây là id suy ra, không có dòng thành viên nào.</summary>
    public Guid UserId { get; set; }

    public string Email { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// <c>false</c> ⇒ người này còn trong nhóm nhưng sẽ KHÔNG nhận thông báo (tài khoản đã nghỉ /
    /// bị đình chỉ / chưa xác thực). Hiển thị mờ để admin biết mà dọn.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>Thời điểm được thêm vào nhóm. <c>null</c> với nhóm <c>Role</c> (không có dòng thật).</summary>
    public DateTime? AddedAt { get; set; }
}
