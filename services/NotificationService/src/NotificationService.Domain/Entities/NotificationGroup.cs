using NotificationService.Domain.Enums;
using SharedKernels.Domain;

namespace NotificationService.Domain.Entities;

/// <summary>
/// Sprint 6.4 NOTI4-01 — nhóm người nhận thông báo, để gửi hàng loạt bằng một lệnh.
///
/// <para>Trước sprint này hệ thống chỉ gửi được cho <b>đúng một người mỗi lệnh</b>
/// (<c>CreateNotificationCommand</c> có duy nhất một <c>Guid UserId</c>), còn "nhóm" chỉ là 4 chuỗi
/// role viết cứng trong code tại 15 chỗ — không tạo/sửa/xoá/đặt tên được.</para>
///
/// <para>Hai loại nhóm — xem <see cref="NotificationGroupKindEnum"/>. Nhóm
/// <see cref="NotificationGroupKindEnum.Static"/> có thành viên tường minh; nhóm
/// <see cref="NotificationGroupKindEnum.Role"/> suy ra thành viên lúc gửi.</para>
/// </summary>
public class NotificationGroup : AuditableEntity
{
    /// <summary>Tên hiển thị, giữ nguyên hoa-thường người dùng gõ.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// <see cref="Name"/> đã chuẩn hoá về CHỮ HOA — cột mang partial unique index chống trùng tên
    /// không phân biệt hoa-thường.
    ///
    /// <para>Phải có cột riêng vì index trên <c>lower(name)</c> là <b>functional index</b>, EF Core
    /// không diễn đạt được bằng <c>HasIndex</c>; nếu chỉ đặt unique trên <c>Name</c> thì "Trực sự cố"
    /// và "trực sự cố" là hai nhóm khác nhau. Đây đúng khuôn <c>Role.NormalizedName</c> mà
    /// AuthService đã dùng — không phát minh cách mới.</para>
    /// </summary>
    public string NormalizedName { get; set; } = string.Empty;

    public string? Description { get; set; }

    public NotificationGroupKindEnum Kind { get; set; } = NotificationGroupKindEnum.Static;

    /// <summary>
    /// Tên role cần khớp — BẮT BUỘC khi <see cref="Kind"/> = <c>Role</c>, phải NULL khi
    /// <c>Static</c> (CHECK constraint ở DB). So khớp không phân biệt hoa-thường.
    /// </summary>
    public string? RoleFilter { get; set; }

    /// <summary>
    /// Nhóm hệ thống (4 nhóm seed theo role). Không cho sửa tên/xoá — chúng là chỗ dựa của
    /// <c>RecipientResolver</c>, xoá đi thì mọi thông báo broadcast trong 13 consumer mất người nhận.
    /// </summary>
    public bool IsSystem { get; set; }

    /// <summary>
    /// Thành viên tường minh. Rỗng với nhóm <see cref="NotificationGroupKindEnum.Role"/>.
    /// Có navigation property vì đây là quan hệ NỘI BỘ trong <c>notification_db</c> — khác với
    /// <c>UserId</c> trỏ sang read-model, chỗ đó cố ý không đặt khoá ngoại.
    /// </summary>
    public ICollection<NotificationGroupMember> Members { get; set; } = new List<NotificationGroupMember>();
}
