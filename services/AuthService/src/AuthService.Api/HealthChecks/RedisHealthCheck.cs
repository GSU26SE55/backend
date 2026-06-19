using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace AuthService.Api.HealthChecks;

/// <summary>
/// #AUTH-60: ready probe — verify Redis reachable bằng PING. Multiplexer cache connection nên
/// chi phí thấp.
/// </summary>
public class RedisHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer _redis;

    public RedisHealthCheck(IConnectionMultiplexer redis) => _redis = redis;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_redis.IsConnected)
                return HealthCheckResult.Unhealthy("Redis multiplexer not connected.");

            var pong = await _redis.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy($"Redis PING ok ({pong.TotalMilliseconds:F1}ms).");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis probe failed.", ex);
        }
    }
}
