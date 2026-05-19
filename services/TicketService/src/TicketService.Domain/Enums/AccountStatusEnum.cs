namespace TicketService.Domain.Enums;

public enum AccountStatusEnum
{
    /// <summary>Tài khoản vừa đăng ký, chưa xác thực email/OTP.</summary>
    PendingVerification = 0,

    /// <summary>Tài khoản đã xác thực và đang hoạt động bình thường.</summary>
    Active = 1,

    /// <summary>Tài khoản bị khóa tạm thời (ví dụ: nhập sai mật khẩu nhiều lần).</summary>
    Locked = 2,

    /// <summary>Tài khoản bị vô hiệu hóa bởi Admin.</summary>
    Inactive = 3,

    /// <summary>Tài khoản bị đình chỉ do vi phạm chính sách.</summary>
    Suspended = 4,

    /// <summary>Tài khoản bị xóa (soft delete cho mục đích nghiệp vụ).</summary>
    Banned = 5
}
