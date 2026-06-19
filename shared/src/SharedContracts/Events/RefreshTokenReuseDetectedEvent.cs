using SharedContracts.Events.Root;

namespace SharedContracts.Events;

/// <summary>
/// #AUTH-79: phát hiện 1 refresh token đã <c>Used</c> bị submit lại — security alert.
/// Một refresh token đã rotate (Used) không bao giờ được submit lần nữa trong normal flow.
/// Nếu thấy = attacker đã steal refresh token và đang race với legitimate client.
///
/// Response: revoke TOÀN BỘ active refresh token của account → buộc user re-login.
/// NotificationService consume để alert user "phiên đăng nhập đáng ngờ — đổi mật khẩu ngay".
/// </summary>
public record RefreshTokenReuseDetectedEvent(
    Guid AccountId,
    Guid ReusedTokenId,
    string? IpAddress,
    string? UserAgent,
    DateTime DetectedAt,
    int RevokedFamilyCount
) : IntegrationEvent;
