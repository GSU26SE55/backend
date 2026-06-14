using System.Text.Json;
using AuthService.Application.Interfaces.Services;
using Microsoft.Extensions.Caching.Distributed;

namespace AuthService.Infrastructure.Implements.Services;

/// <summary>
/// Redis-backed pending store cho flow init→confirm enable 2FA. Key <c>2fa:pending:{accountId}</c>.
/// Dùng <see cref="IDistributedCache"/> (đủ đơn giản — không cần atomic counter như challenge).
/// </summary>
public class TwoFactorPendingStore : ITwoFactorPendingStore
{
    private const string KeyPrefix = "2fa:pending:";

    private readonly IDistributedCache _cache;

    public TwoFactorPendingStore(IDistributedCache cache) => _cache = cache;

    public async Task SetAsync(Guid accountId, string secret, string pendingToken, TimeSpan ttl, CancellationToken ct = default)
    {
        var payload = new PendingPayload(secret, pendingToken, DateTime.UtcNow);
        var json = JsonSerializer.Serialize(payload);
        await _cache.SetStringAsync(KeyPrefix + accountId, json, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl
        }, ct);
    }

    public async Task<TwoFactorPendingData?> GetAsync(Guid accountId, CancellationToken ct = default)
    {
        var json = await _cache.GetStringAsync(KeyPrefix + accountId, ct);
        if (string.IsNullOrEmpty(json))
            return null;
        var payload = JsonSerializer.Deserialize<PendingPayload>(json);
        if (payload is null)
            return null;
        return new TwoFactorPendingData(payload.Secret, payload.PendingToken, payload.CreatedAtUtc);
    }

    public Task RemoveAsync(Guid accountId, CancellationToken ct = default)
        => _cache.RemoveAsync(KeyPrefix + accountId, ct);

    private sealed record PendingPayload(string Secret, string PendingToken, DateTime CreatedAtUtc);
}
