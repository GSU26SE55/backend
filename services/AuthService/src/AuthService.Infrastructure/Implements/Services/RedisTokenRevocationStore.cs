using AuthService.Application.Interfaces.Services;
using StackExchange.Redis;

namespace AuthService.Infrastructure.Implements.Services;

/// <summary>
/// #AUTH-54: Redis-backed Token Revocation List.
/// Keys:
/// - <c>revoked_jti:{jti}</c> — single token blacklist với TTL = remaining token life.
/// - <c>account_revoke_cutoff:{accountId}</c> — Unix timestamp (UTC seconds). Token issued
///   trước cutoff coi như revoked (bulk).
/// </summary>
public class RedisTokenRevocationStore : ITokenRevocationStore
{
    private const string JtiKeyPrefix = "revoked_jti:";
    private const string AccountCutoffKeyPrefix = "account_revoke_cutoff:";

    private readonly IConnectionMultiplexer _redis;

    public RedisTokenRevocationStore(IConnectionMultiplexer redis) => _redis = redis;

    public async Task RevokeAsync(string jti, TimeSpan ttl, string? reason = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(jti))
            return;
        if (ttl <= TimeSpan.Zero)
            return; // Token đã expired tự nhiên — không cần blacklist.
        var db = _redis.GetDatabase();
        await db.StringSetAsync(JtiKeyPrefix + jti, reason ?? "revoked", ttl);
    }

    public async Task<bool> IsRevokedAsync(string jti, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(jti))
            return false;
        var db = _redis.GetDatabase();
        return await db.KeyExistsAsync(JtiKeyPrefix + jti);
    }

    public async Task RevokeAllByAccountAsync(Guid accountId, TimeSpan ttl, CancellationToken ct = default)
    {
        if (accountId == Guid.Empty)
            return;
        if (ttl <= TimeSpan.Zero)
            return;
        var db = _redis.GetDatabase();
        var cutoffUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        await db.StringSetAsync(AccountCutoffKeyPrefix + accountId.ToString("N"), cutoffUnix, ttl);
    }

    public async Task<bool> IsAccountFullyRevokedAsync(Guid accountId, DateTime tokenIssuedAt, CancellationToken ct = default)
    {
        if (accountId == Guid.Empty)
            return false;
        var db = _redis.GetDatabase();
        var raw = await db.StringGetAsync(AccountCutoffKeyPrefix + accountId.ToString("N"));
        if (!raw.HasValue || !long.TryParse(raw, out var cutoffUnix))
            return false;
        var tokenUnix = ((DateTimeOffset)DateTime.SpecifyKind(tokenIssuedAt, DateTimeKind.Utc)).ToUnixTimeSeconds();
        return tokenUnix < cutoffUnix;
    }
}
