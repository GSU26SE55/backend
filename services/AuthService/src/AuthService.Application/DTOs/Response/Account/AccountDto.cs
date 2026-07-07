using AuthService.Domain.Enums;

namespace AuthService.Application.DTOs.Response.Account;

public class AccountDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = null!;
    public string? PhoneNumber { get; set; }
    public string FullName { get; set; } = null!;
    public string? AvatarUrl { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? Address { get; set; }
    public bool EmailConfirmed { get; set; }
    public bool PhoneConfirmed { get; set; }
    public bool TwoFactorEnabled { get; set; }

    /// <summary>true nếu account đã liên kết đăng nhập Google (GoogleId != null) — màn Cài đặt FE dùng để hiện "Liên kết"/"Hủy liên kết".</summary>
    public bool IsGoogleLinked { get; set; }

    public AccountStatusEnum Status { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    /// <summary>Id của role hiện tại của account (quan hệ 1-N: Role → Account). #AUTH-69: nullable.</summary>
    public Guid? RoleId { get; set; }

    /// <summary>Tên role hiện tại — vd "Admin", "Customer". Có thể empty nếu role bị xóa/disable.</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>Thời điểm role được gán/đổi lần cuối.</summary>
    public DateTime? RoleAssignedAt { get; set; }

    /// <summary>AccountId của người gán/đổi role lần cuối — null nếu là seed/self-register.</summary>
    public Guid? RoleAssignedBy { get; set; }
    public AccountProfileDto? Profile { get; set; }
    public StaffProfileDto? StaffProfile { get; set; }
    public string? DisplayAvatarUrl { get; set; }
}
