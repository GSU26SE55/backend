namespace NotificationService.Domain.Enums;

/// <summary>
/// Sprint 6.4 NOTI4-01 — cách một nhóm xác định thành viên của nó. Enum bắt đầu từ 1.
/// </summary>
public enum NotificationGroupKindEnum
{
    /// <summary>
    /// Thành viên liệt kê tường minh trong <c>notification_group_members</c>. Admin thêm/bớt tay.
    /// Ví dụ: "Trực sự cố cuối tuần", "Khách hàng VIP".
    /// </summary>
    Static = 1,

    /// <summary>
    /// Thành viên SUY RA lúc gửi: mọi account đang hoạt động có role khớp
    /// <c>NotificationGroup.RoleFilter</c>. Không có dòng nào trong bảng thành viên.
    ///
    /// <para>Loại này tồn tại không phải để cho đủ bộ — nó là đường di trú cho 15 chỗ hard-code
    /// chuỗi role trong consumer. Có nó thì <c>RecipientResolver</c> đổi phần ruột sang tra nhóm mà
    /// 13 file consumer không phải sửa một dòng nào.</para>
    /// </summary>
    Role = 2,
}
