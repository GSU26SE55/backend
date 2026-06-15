using System.Text.Json;
using AuthService.Application.Interfaces.Services;
using StackExchange.Redis;

namespace AuthService.Infrastructure.Implements.Services;

/// <summary>
/// Redis-backed challenge store. Key <c>2fa:challenge:{token}</c>. Field <c>data</c> JSON, field <c>attempts</c> counter atomic.
/// </summary>
public class TwoFactorChallengeStore : ITwoFactorChallengeStore
{
    private const string KeyPrefix = "2fa:challenge:";
    private const string FieldData = "data";
    private const string FieldAttempts = "attempts";

    private readonly IConnectionMultiplexer _redis;

    public TwoFactorChallengeStore(IConnectionMultiplexer redis) => _redis = redis;

    public async Task<string> CreateAsync(Guid accountId, string ipAddress, string userAgent, TimeSpan ttl, CancellationToken ct = default)
    {
        var token = Guid.NewGuid().ToString("N");
        var key = KeyPrefix + token;
        var db = _redis.GetDatabase();

        var payload = new ChallengePayload(accountId, ipAddress, userAgent, DateTime.UtcNow);
        var json = JsonSerializer.Serialize(payload);

        await db.HashSetAsync(key, new[]
        {
            new HashEntry(FieldData, json),
            new HashEntry(FieldAttempts, 0),
        });
        await db.KeyExpireAsync(key, ttl);
        return token;
    }

    public async Task<TwoFactorChallengeData?> GetAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;
        var db = _redis.GetDatabase();
        var key = KeyPrefix + token;
        var entries = await db.HashGetAllAsync(key);
        if (entries.Length == 0)
            return null;

        string? json = null;
        int attempts = 0;
        foreach (var e in entries)
        {
            if (e.Name == FieldData)
                json = e.Value;
            else if (e.Name == FieldAttempts)
                attempts = (int)e.Value;
        }
        if (string.IsNullOrEmpty(json))
            return null;

        var payload = JsonSerializer.Deserialize<ChallengePayload>(json);
        if (payload is null)
            return null;
        return new TwoFactorChallengeData(payload.AccountId, payload.IpAddress, payload.UserAgent, attempts, payload.CreatedAtUtc);
    }

    public async Task<int> IncrementAttemptsAsync(string token, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var key = KeyPrefix + token;
        var newValue = await db.HashIncrementAsync(key, FieldAttempts);
        return (int)newValue;
    }

    public async Task InvalidateAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return;
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync(KeyPrefix + token);
    }

    private sealed record ChallengePayload(Guid AccountId, string IpAddress, string UserAgent, DateTime CreatedAtUtc);
}
