namespace TicketService.Domain.Enums;

/// <summary>
/// Trạng thái tài khoản người dùng trong hệ thống.
///
/// #AUTH-18: phân biệt rõ semantic Locked vs Inactive:
/// - <see cref="Locked"/>: tạm thời, hệ thống tự đặt từ failed-login threshold. Auto-unlock khi
///   LockoutEndAt &lt;= now (handler check tại login + scheduled job
///   <c>LockoutReconcileBackgroundService</c> chạy mỗi 5 phút).
/// - <see cref="Inactive"/>: vĩnh viễn (cho đến khi admin reactivate). Set bởi admin operation
///   (<c>AdminDeactivateAccount</c>). KHÔNG có auto-unlock — user phải request admin support.
/// - <see cref="Suspended"/>: vi phạm policy. Set + clear bởi admin only.
/// - <see cref="Banned"/>: kết quả của hard policy violation. Không reactivate được, phải tạo account mới.
/// </summary>
public enum AccountStatusEnum
{
    /// <summary>Tài khoản vừa đăng ký, chưa xác thực email/OTP.</summary>
    PendingVerification = 0,

    /// <summary>Tài khoản đã xác thực và đang hoạt động bình thường.</summary>
    Active = 1,

    /// <summary>
    /// Tài khoản bị KHÓA TẠM THỜI bởi system (5 lần fail password/OTP liên tiếp).
    /// Auto-unlock khi LockoutEndAt qua hoặc scheduled job dọn.
    /// </summary>
    Locked = 2,

    /// <summary>
    /// Tài khoản bị VÔ HIỆU HÓA bởi Admin (deactivate). KHÔNG auto-unlock — user phải request admin.
    /// </summary>
    Inactive = 3,

    /// <summary>Tài khoản bị đình chỉ do vi phạm chính sách. Admin only set/clear.</summary>
    Suspended = 4,

    /// <summary>Tài khoản bị cấm vĩnh viễn do vi phạm nghiêm trọng.</summary>
    Banned = 5
}
