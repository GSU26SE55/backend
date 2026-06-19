namespace AuthService.Application.Interfaces.Services;

/// <summary>
/// #AUTH-54: Token Revocation List (TRL) — Redis-backed blacklist cho JWT jti.
///
/// Set khi: logout, password change, admin force-logout, permission revoke (#AUTH-15).
/// Check khi: mỗi request có Authorization header (qua middleware).
/// TTL = remaining token life để tự dọn sau khi token expire.
/// </summary>
public interface ITokenRevocationStore
{
    /// <summary>Đánh dấu jti là revoked. <paramref name="ttl"/> = remaining token life.</summary>
    Task RevokeAsync(string jti, TimeSpan ttl, string? reason = null, CancellationToken ct = default);

    /// <summary>true nếu jti đã trong blacklist (đã revoke).</summary>
    Task<bool> IsRevokedAsync(string jti, CancellationToken ct = default);

    /// <summary>Bulk revoke theo accountId — dùng khi admin force-logout user.</summary>
    Task RevokeAllByAccountAsync(Guid accountId, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>true nếu account đã bị bulk-revoke (mọi token issued trước cutoff đều invalid).</summary>
    Task<bool> IsAccountFullyRevokedAsync(Guid accountId, DateTime tokenIssuedAt, CancellationToken ct = default);
}
