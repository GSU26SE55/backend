using AuthService.Domain.Enums;
using SharedKernels.Domain;

namespace AuthService.Domain.Entities;

/// <summary>
/// Đại diện cho một tài khoản người dùng (Admin, Manager, Staff, Customer) trong hệ thống.
/// Mỗi account chỉ có duy nhất 1 Role (quan hệ 1-N: 1 Role → nhiều Account).
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

    /// <summary>
    /// Thời điểm <see cref="TwoFactorSecret"/> được encrypt qua Data Protection.
    /// Null = legacy plaintext (chưa migrate). Set bởi lazy re-encrypt khi user verify TOTP lần đầu sau migration,
    /// hoặc set ngay khi user enroll mới qua <c>/2fa/confirm</c>.
    /// </summary>
    public DateTime? TwoFactorSecretEncryptedAt { get; set; }

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

    /// <summary>Role hiện tại của account. Mỗi account bắt buộc có đúng 1 role.</summary>
    public Guid RoleId { get; set; }

    /// <summary>Thời điểm role được gán/đổi lần cuối — audit "ai đổi khi nào".</summary>
    public DateTime? RoleAssignedAt { get; set; }

    /// <summary>AccountId của admin đã gán/đổi role; null nếu là seed hoặc self-register.</summary>
    public Guid? RoleAssignedBy { get; set; }

    public Role Role { get; set; } = null!;

    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();

    public AccountProfile? Profile { get; set; }

    public StaffProfile? StaffProfile { get; set; }

    public ICollection<BackupCode> BackupCodes { get; set; } = new List<BackupCode>();
}
