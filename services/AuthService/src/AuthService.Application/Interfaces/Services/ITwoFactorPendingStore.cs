namespace AuthService.Application.Interfaces.Services;

/// <summary>
/// Lưu pending state giữa <c>POST /api/accounts/me/2fa/init</c> và <c>POST /api/accounts/me/2fa/confirm</c>.
/// Backed by Redis, key <c>2fa:pending:{accountId}</c>, TTL 10 phút.
/// </summary>
public interface ITwoFactorPendingStore
{
    /// <summary>Lưu secret + pendingToken cho account. Overwrite nếu đã có (cho phép user init lại).</summary>
    Task SetAsync(Guid accountId, string secret, string pendingToken, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>Lấy pending data; null nếu hết hạn hoặc chưa init.</summary>
    Task<TwoFactorPendingData?> GetAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>Xóa pending — gọi sau khi confirm thành công.</summary>
    Task RemoveAsync(Guid accountId, CancellationToken ct = default);
}

public sealed record TwoFactorPendingData(string Secret, string PendingToken, DateTime CreatedAtUtc);
