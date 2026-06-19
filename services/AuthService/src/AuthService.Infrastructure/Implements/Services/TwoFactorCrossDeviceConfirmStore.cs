using System.Text.Json;
using AuthService.Application.Interfaces.Services;
using Microsoft.Extensions.Caching.Distributed;

namespace AuthService.Infrastructure.Implements.Services;

/// <summary>
/// #AUTH-51: Redis-backed store cho cross-device 2FA confirm token.
/// Key <c>2fa:confirm-token:{token}</c>. TTL configurable (default 10 phút).
/// </summary>
public class TwoFactorCrossDeviceConfirmStore : ITwoFactorCrossDeviceConfirmStore
{
    private const string KeyPrefix = "2fa:confirm-token:";
    private readonly IDistributedCache _cache;

    public TwoFactorCrossDeviceConfirmStore(IDistributedCache cache) => _cache = cache;

    public async Task SetAsync(string token, Guid accountId, string secret, string requestingSessionId, TimeSpan ttl, CancellationToken ct = default)
    {
        var payload = new Payload(accountId, secret, requestingSessionId, DateTime.UtcNow);
        var json = JsonSerializer.Serialize(payload);
        await _cache.SetStringAsync(KeyPrefix + token, json, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl
        }, ct);
    }

    public async Task<TwoFactorCrossDeviceConfirmData?> GetAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;
        var json = await _cache.GetStringAsync(KeyPrefix + token, ct);
        if (string.IsNullOrEmpty(json))
            return null;
        var payload = JsonSerializer.Deserialize<Payload>(json);
        if (payload is null)
            return null;
        return new TwoFactorCrossDeviceConfirmData(payload.AccountId, payload.Secret, payload.RequestingSessionId, payload.CreatedAtUtc);
    }

    public Task RemoveAsync(string token, CancellationToken ct = default)
        => _cache.RemoveAsync(KeyPrefix + token, ct);

    private sealed record Payload(Guid AccountId, string Secret, string RequestingSessionId, DateTime CreatedAtUtc);
}
