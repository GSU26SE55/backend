using SharedKernels.Domain;

namespace AuthService.Domain.Entities;

/// <summary>
/// #AUTH-48: Thiết bị được user đánh dấu "trust" để skip 2FA challenge trong TTL.
///
/// Mỗi row = 1 device fingerprint cho 1 account. User tick "Trust this device" lúc verify 2FA
/// → row được tạo với <see cref="ExpiresAt"/> = now + 30d (configurable). Login lần sau từ device
/// match fingerprint + IP /24 prefix → LoginCommandHandler skip challenge, issue JWT trực tiếp.
///
/// Security trade-off:
/// - Trust chỉ được set sau khi user đã pass password + 2FA (KHÔNG cấp lúc verify backup code recovery —
///   backup code dành cho emergency, không nên trust device emergency).
/// - Auto-revoke khi user đổi password (<c>ChangePasswordCommandHandler</c>) hoặc disable 2FA.
/// - Endpoint <c>DELETE /api/me/trusted-devices/{id}</c> cho user revoke thủ công.
/// - IP /24 match (không phải full IP) → tolerate DHCP đổi IP cùng subnet, không cho phép từ mạng lạ.
/// </summary>
public class TrustedDevice : AuditableEntity
{
    public Guid AccountId { get; set; }

    /// <summary>
    /// SHA-256 hash của (DeviceId + UserAgent) — KHÔNG lưu raw fingerprint (PII).
    /// 64 hex chars.
    /// </summary>
    public string DeviceFingerprintHash { get; set; } = null!;

    /// <summary>
    /// /24 prefix của IPv4 (vd "203.0.113.0/24") hoặc /64 IPv6. Cho phép tolerate DHCP cùng subnet.
    /// </summary>
    public string IpPrefix { get; set; } = null!;

    /// <summary>
    /// User-friendly label do user nhập (vd "MacBook nhà") hoặc auto-generate từ UA ("Chrome on macOS").
    /// </summary>
    public string Label { get; set; } = null!;

    /// <summary>UserAgent đầy đủ lúc trust — display only, KHÔNG dùng để match (đã include trong fingerprint).</summary>
    public string? UserAgentSnapshot { get; set; }

    public DateTime TrustedAt { get; set; } = DateTime.UtcNow;

    /// <summary>TTL = TrustedAt + 30 ngày (configurable). Sau hạn → row coi như invalid, KHÔNG auto-delete để giữ audit.</summary>
    public DateTime ExpiresAt { get; set; }

    public DateTime? LastUsedAt { get; set; }

    /// <summary>
    /// Số lần device này skip 2FA challenge (audit metric). Increment trong login flow khi match.
    /// </summary>
    public int UsageCount { get; set; }

    /// <summary>
    /// Khi user revoke thủ công, đổi password, hoặc disable 2FA → set flag này thay vì hard-delete.
    /// </summary>
    public DateTime? RevokedAt { get; set; }

    public string? RevokedReason { get; set; }

    public Account Account { get; set; } = null!;

    public bool IsActiveAt(DateTime now) => RevokedAt == null && ExpiresAt > now && !IsDeleted;
}
