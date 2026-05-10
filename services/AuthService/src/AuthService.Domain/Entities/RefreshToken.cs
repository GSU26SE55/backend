using AuthService.Domain.Enums;
using SharedKernels.Domain;

namespace AuthService.Domain.Entities;

/// <summary>
/// Refresh token theo cơ chế rotation. Mỗi lần refresh sẽ tạo token mới và đánh dấu token cũ là Used.
/// Hỗ trợ phát hiện replay attack qua trường ReplacedByToken.
/// </summary>
public class RefreshToken : AuditableEntity
{
    public Guid AccountId { get; set; }

    public string Token { get; set; } = null!;

    public string? JwtId { get; set; }

    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;

    public DateTime ExpiredAt { get; set; }

    public RefreshTokenStatus Status { get; set; } = RefreshTokenStatus.Active;

    public DateTime? UsedAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public string? RevokedReason { get; set; }

    public string? ReplacedByToken { get; set; }

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public string? DeviceId { get; set; }

    public Account Account { get; set; } = null!;

    public bool IsExpired => DateTime.UtcNow >= ExpiredAt;

    public bool IsActive => Status == RefreshTokenStatus.Active && !IsExpired;
}
