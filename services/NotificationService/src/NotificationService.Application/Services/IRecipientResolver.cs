namespace NotificationService.Application.Services;

/// <summary>
/// GH-604 — Resolve recipient thật cho notification từ read-model account
/// (<see cref="NotificationService.Domain.Entities.AccountReadModel"/>).
///
/// <para>Sprint 6.4 NOTI4-05 — bổ sung <see cref="GetGroupRecipientsAsync"/> để gửi theo nhóm. Cả
/// hai phương thức dùng CHUNG một định nghĩa "ai đủ điều kiện nhận"
/// (<c>NotificationGroupMembership.NotifiableAccounts</c>): còn hoạt động và chưa xoá.</para>
/// </summary>
public interface IRecipientResolver
{
    /// <summary>
    /// Trả về danh sách AccountId của các account đang active có role khớp <paramref name="roles"/>
    /// (so khớp không phân biệt hoa-thường). Rỗng nếu không có account nào — caller phải skip + log,
    /// KHÔNG ghi notification với recipient rỗng.
    ///
    /// <para><b>Sprint 6.4 — cố ý KHÔNG định tuyến qua nhóm <c>Role</c>.</b> Kế hoạch ban đầu định
    /// đổi phần ruột hàm này sang tra nhóm, với lý do "biến 15 chỗ hard-code role thành dữ liệu".
    /// Khi thi công thì thấy lợi ích đó không có thật: nhóm <c>Role</c> gắn cứng với đúng một tên
    /// role, nên đổi "sự kiện này báo cho ai" vẫn phải sửa code y như cũ — chỉ thêm một tầng gián
    /// tiếp. Đổi lại, cái giá thì có thật: toàn bộ thông báo tự động (SLA, ticket, cảnh báo pin) sẽ
    /// phụ thuộc vào 4 dòng seed; thiếu hoặc lỡ xoá một dòng là hàm này trả rỗng và mọi thông báo
    /// cho vai trò đó <i>im lặng</i> biến mất — đúng lớp lỗi mà NOTI4-00 vừa phải đi sửa.</para>
    ///
    /// <para>Nhóm <c>Role</c> vẫn có giá trị, nhưng ở chỗ khác: để admin <b>chọn</b> "toàn bộ Quản
    /// lý" trên màn hình gửi hàng loạt — xem <see cref="GetGroupRecipientsAsync"/>.</para>
    /// </summary>
    Task<IReadOnlyList<Guid>> GetActiveByRoleAsync(CancellationToken cancellationToken, params string[] roles);

    /// <summary>
    /// Sprint 6.4 NOTI4-05 — trả về AccountId của người nhận thuộc các nhóm chỉ định, đã
    /// <b>gom trùng</b> và đã loại người không còn hoạt động.
    ///
    /// <para>Nhận cả hai loại nhóm: <c>Static</c> đọc bảng thành viên, <c>Role</c> suy ra từ
    /// read-model. Người thuộc nhiều nhóm cùng được nhắm chỉ xuất hiện <b>một lần</b> — đây là lớp
    /// chống nhận trùng thứ nhất; lớp thứ hai là unique index
    /// <c>ux_notifications_batch_user_channel</c> ở DB.</para>
    ///
    /// <para>Nhóm không tồn tại hoặc đã xoá bị bỏ qua — caller tự đối chiếu số lượng nếu cần biết.</para>
    /// </summary>
    Task<IReadOnlyList<Guid>> GetGroupRecipientsAsync(
        IEnumerable<Guid> groupIds, CancellationToken cancellationToken);
}
