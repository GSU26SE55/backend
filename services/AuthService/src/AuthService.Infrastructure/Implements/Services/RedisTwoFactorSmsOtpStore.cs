using AuthService.Application.Interfaces.Services;
using StackExchange.Redis;

namespace AuthService.Infrastructure.Implements.Services;

/// <summary>#AUTH-58: Redis impl cho <see cref="ITwoFactorSmsOtpStore"/>. Key <c>2fa:sms_otp:{challengeToken}</c>.</summary>
public class RedisTwoFactorSmsOtpStore : ITwoFactorSmsOtpStore
{
    private const string KeyPrefix = "2fa:sms_otp:";

    private readonly IConnectionMultiplexer _redis;

    public RedisTwoFactorSmsOtpStore(IConnectionMultiplexer redis) => _redis = redis;

    public async Task SetAsync(string challengeToken, string otp, TimeSpan ttl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(challengeToken) || string.IsNullOrWhiteSpace(otp))
            return;
        await _redis.GetDatabase().StringSetAsync(KeyPrefix + challengeToken, otp, ttl);
    }

    public async Task<string?> GetAsync(string challengeToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(challengeToken))
            return null;
        var val = await _redis.GetDatabase().StringGetAsync(KeyPrefix + challengeToken);
        return val.HasValue ? val.ToString() : null;
    }

    public async Task InvalidateAsync(string challengeToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(challengeToken))
            return;
        await _redis.GetDatabase().KeyDeleteAsync(KeyPrefix + challengeToken);
    }
}
