namespace AuthService.Domain.Enums;

/// <summary>
/// 02/08/2026 — Quy tắc DUY NHẤT trả lời câu hỏi "tài khoản này còn được nhận thông báo không",
/// dùng khi dựng <c>AccountSyncSnapshotEvent</c>.
///
/// Có quy tắc dùng chung vì nó được áp ở nhiều handler rời nhau (đổi trạng thái, đổi role, khôi
/// phục tài khoản, tự vô hiệu hoá, seed, đối soát). Mỗi nơi tự viết lại điều kiện là kiểu gì cũng
/// có nơi lệch, mà lệch ở đây thì hậu quả là người đáng nhận cảnh báo lại không nhận được —
/// không có gì báo lỗi, chỉ im lặng bỏ qua.
/// </summary>
public static class AccountStatusEnumExtensions
{
    /// <summary>
    /// <c>true</c> khi tài khoản còn nằm trong danh sách nhận thông báo.
    ///
    /// <para><b>Vì sao <see cref="AccountStatusEnum.Locked"/> vẫn tính là còn nhận:</b> Locked là
    /// khoá TẠM do sai mật khẩu 5 lần, tự hết hạn và tự chuyển lại Active ở lần đăng nhập kế
    /// (<c>LoginCommandHandler</c>). Nếu coi Locked là ngừng nhận thì một Manager gõ nhầm mật khẩu
    /// sẽ không nhận được email/SMS cảnh báo SLA P1 trong suốt thời gian khoá — đây là thiệt hại
    /// thật, trong khi cái được thì không có: chặn thông báo không làm tài khoản an toàn hơn.
    ///
    /// Chọn như vậy còn có một hệ quả tốt: cặp chuyển trạng thái Active ↔ Locked KHÔNG làm đổi
    /// giá trị này, nên <c>LoginCommandHandler</c> (đường nóng nhất hệ thống) và
    /// <c>UnlockAccountCommandHandler</c> không cần phát snapshot, mà read-model vẫn không lệch.</para>
    ///
    /// <para>Các trạng thái còn lại đều là ngừng phục vụ có chủ đích và không tự hết hạn:
    /// <see cref="AccountStatusEnum.PendingVerification"/> (chưa xác thực),
    /// <see cref="AccountStatusEnum.Inactive"/> (admin hoặc chính chủ vô hiệu hoá),
    /// <see cref="AccountStatusEnum.Suspended"/> (đình chỉ),
    /// <see cref="AccountStatusEnum.Banned"/> (cấm vĩnh viễn).</para>
    /// </summary>
    public static bool IsNotifiable(this AccountStatusEnum status) =>
        status is AccountStatusEnum.Active or AccountStatusEnum.Locked;
}
