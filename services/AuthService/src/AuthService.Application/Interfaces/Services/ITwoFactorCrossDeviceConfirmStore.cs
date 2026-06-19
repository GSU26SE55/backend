namespace AuthService.Application.Interfaces.Services;

/// <summary>
/// #AUTH-51: Lưu cross-device confirm token cho flow setup 2FA xuyên thiết bị.
///
/// Use case: user setup 2FA trên Laptop nhưng Laptop không có camera scan QR → mở email từ Phone,
/// click link → confirm 2FA enable cho Laptop session.
///
/// Backed by Redis, key <c>2fa:confirm-token:{token}</c>, TTL 10 phút.
/// Lưu (AccountId, Secret, RequestingSessionId, ExpiresAt) — token chính là key, không lưu reverse-index.
/// </summary>
public interface ITwoFactorCrossDeviceConfirmStore
{
    /// <summary>
    /// Lưu confirm token (random secure 32 bytes hex) → payload. TTL 10 phút.
    /// Throw nếu token đã tồn tại (collision cực hiếm với 32 bytes random, fail-fast).
    /// </summary>
    Task SetAsync(string token, Guid accountId, string secret, string requestingSessionId, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>Lấy payload từ token. Null nếu hết hạn hoặc token sai.</summary>
    Task<TwoFactorCrossDeviceConfirmData?> GetAsync(string token, CancellationToken ct = default);

    /// <summary>Xóa token sau khi confirm thành công (one-shot). Idempotent.</summary>
    Task RemoveAsync(string token, CancellationToken ct = default);
}

public sealed record TwoFactorCrossDeviceConfirmData(
    Guid AccountId,
    string Secret,
    string RequestingSessionId,
    DateTime CreatedAtUtc);
