using AuthService.Domain.Enums;
using SharedKernels.Domain;

namespace AuthService.Domain.Entities;

/// <summary>
/// Lịch sử login attempt — append-only.
/// Khác với <see cref="AuditLog"/>: chỉ chứa thông tin login (success/fail) để hiển thị cho user
/// trong endpoint "device history". Audit log cho toàn bộ hành động nhạy cảm khác.
///
/// User có thể xem login history của chính mình; admin xem được của bất kỳ account.
/// </summary>
public class LoginAttempt : AuditableEntity
{
    /// <summary>Account id nếu tìm được theo email — null nếu email không tồn tại.</summary>
    public Guid? AccountId { get; set; }

    /// <summary>Email mà client submit (normalized).</summary>
    public string AttemptedEmail { get; set; } = string.Empty;

    public LoginAttemptResult Result { get; set; }

    /// <summary>Phương thức login: "Password", "Google", "VerifyOtp".</summary>
    public string Method { get; set; } = string.Empty;

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public string? DeviceId { get; set; }

    /// <summary>Note bổ sung (vd: số lần thử còn lại, lý do lock).</summary>
    public string? Note { get; set; }

    public Account? Account { get; set; }
}
