using AuthService.Domain.Enums;
using SharedKernels.Domain;

namespace AuthService.Domain.Entities;

/// <summary>
/// Đại diện cho một tài khoản người dùng (Admin, Technician, Customer...) trong hệ thống.
/// </summary>
public class Account : AuditableEntity
{
    public string Email { get; set; } = null!;

    public string? PhoneNumber { get; set; }

    public string PasswordHash { get; set; } = null!;

    public string FullName { get; set; } = null!;

    public string? AvatarUrl { get; set; }

    public DateTime? DateOfBirth { get; set; }

    public string? Address { get; set; }

    public bool EmailConfirmed { get; set; } = false;

    public bool PhoneConfirmed { get; set; } = false;

    public bool TwoFactorEnabled { get; set; } = false;

    public string? OtpCode { get; set; }

    public DateTime? OtpExpiredAt { get; set; }

    public OtpPurposeEnum? OtpPurpose { get; set; }

    /// <summary>Email mới đang chờ verify trong luồng change-email. Khi confirm OTP đúng thì copy sang Email và xoá field này.</summary>
    public string? PendingEmail { get; set; }

    public string? TwoFactorSecret { get; set; }

    public int FailedLoginAttempts { get; set; } = 0;

    public DateTime? LockoutEndAt { get; set; }

    public DateTime? LastLoginAt { get; set; }

    public string? LastLoginIp { get; set; }

    public AccountStatusEnum Status { get; set; } = AccountStatusEnum.PendingVerification;

    public string? GoogleId { get; set; }

    public string? Provider { get; set; }

    /// <summary>
    /// Token dùng cho admin invite flow. Khi admin tạo account ở chế độ invite (không set password sẵn),
    /// hệ thống sinh token này và gửi email mời. User truy cập link <c>?token=...</c> để kích hoạt
    /// và đặt password lần đầu. Clear sau khi accept invite thành công.
    /// </summary>
    public string? InvitationToken { get; set; }

    public DateTime? InvitationExpiredAt { get; set; }

    public ICollection<AccountRole> AccountRoles { get; set; } = new List<AccountRole>();

    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();

    public AccountProfile? Profile { get; set; }

    public StaffProfile? StaffProfile { get; set; }
}
