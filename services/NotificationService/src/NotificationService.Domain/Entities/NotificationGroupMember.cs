using SharedKernels.Domain;

namespace NotificationService.Domain.Entities;

/// <summary>
/// Sprint 6.4 NOTI4-01 — bảng nối quan hệ <b>nhiều-nhiều</b> giữa người dùng và nhóm:
/// một người ở nhiều nhóm, một nhóm có nhiều người.
///
/// <para>Chỉ áp dụng cho nhóm <see cref="Enums.NotificationGroupKindEnum.Static"/>. Nhóm
/// <c>Role</c> suy ra thành viên lúc gửi nên không có dòng nào ở đây.</para>
/// </summary>
public class NotificationGroupMember : AuditableEntity
{
    /// <summary>Khoá ngoại thật (<c>ON DELETE CASCADE</c>) — cùng database, cùng transaction.</summary>
    public Guid GroupId { get; set; }

    public NotificationGroup? Group { get; set; }

    /// <summary>
    /// AccountId bên AuthService. <b>KHÔNG</b> đặt khoá ngoại sang <c>account_read_models</c>.
    ///
    /// <para>Lý do: read-model đồng bộ qua message bus, mà thứ tự message không được bảo đảm — thêm
    /// người vào nhóm ngay sau khi tạo tài khoản có thể chạm lúc snapshot đồng bộ chưa tới, insert
    /// sẽ vỡ vì vi phạm khoá rồi retry, có khi hết lượt vẫn hỏng. Đổi lại được rất ít, vì nguồn sự
    /// thật nằm ở <c>auth_db</c> của service khác — khoá ngoại nội bộ không bảo vệ được gì trước
    /// sai lệch xuyên service.</para>
    ///
    /// <para>Việc loại người đã nghỉ/bị đình chỉ làm bằng <b>JOIN lúc gửi</b> (lọc
    /// <c>AccountReadModel.IsActive</c>), không bằng ràng buộc DB. Thành viên trỏ tới account đã
    /// biến mất thì để nguyên, chỉ không được chọn khi gửi.</para>
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>Admin nào đã thêm người này vào nhóm.</summary>
    public Guid? AddedBy { get; set; }
}
