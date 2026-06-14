using SharedKernels.Domain;

namespace AuthService.Domain.Entities;

/// <summary>
/// Backup code (recovery code) cho 2FA — cho phép user đăng nhập khi mất Authenticator device.
/// Mỗi account có tối đa 8 codes; mỗi code dùng được 1 lần (single-use); hash bằng BCrypt.
/// Khi user disable 2FA hoặc admin reset 2FA → xóa hết codes của account.
/// </summary>
public class BackupCode : AuditableEntity
{
    public Guid AccountId { get; set; }

    /// <summary>BCrypt hash của plaintext code (đã normalize lowercase + bỏ dash).</summary>
    public string CodeHash { get; set; } = null!;

    /// <summary>Thời điểm code này được dùng (single-use). Null = chưa dùng.</summary>
    public DateTime? RedeemedAt { get; set; }

    public Account Account { get; set; } = null!;
}
