using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using SharedContracts.Interfaces;
using StackExchange.Redis;

namespace SharedInfrastructure.Caching;

public class RedisCacheService : ICacheService
{
    private const string CompareAndExpireScript = "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('pexpire', KEYS[1], ARGV[2]) else return 0 end";
    private const string CompareAndDeleteScript = "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end";
    private readonly IDistributedCache _cache;
    private readonly IConnectionMultiplexer? _redis;
    private readonly ILogger<RedisCacheService>? _logger;

    /// <summary>
    /// Sprint 6.3 NOTI3-09 (#709) — nhận thêm <see cref="IConnectionMultiplexer"/> để làm được
    /// <c>SET NX</c> atomic; <see cref="IDistributedCache"/> không có API nào tương đương.
    /// Hai tham số thêm đều optional nên mọi nơi đang gọi <c>new RedisCacheService(cache)</c>
    /// vẫn biên dịch nguyên vẹn (chỉ mất tính atomic, rơi về fallback có ghi log).
    /// </summary>
    public RedisCacheService(
        IDistributedCache cache,
        IConnectionMultiplexer? redis = null,
        ILogger<RedisCacheService>? logger = null)
    {
        _cache = cache;
        _redis = redis;
        _logger = logger;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var cachedData = await _cache.GetStringAsync(key, cancellationToken);
        if (string.IsNullOrEmpty(cachedData))
            return default;

        return JsonSerializer.Deserialize<T>(cachedData);
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiration ?? TimeSpan.FromMinutes(10)
        };

        var json = JsonSerializer.Serialize(value);
        await _cache.SetStringAsync(key, json, options, cancellationToken);
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        await _cache.RemoveAsync(key, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> TrySetIfNotExistsAsync(
        string key, string value, TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        if (_redis is not null)
        {
            // Đường atomic thật: 1 lệnh SET key val NX EX ttl.
            var db = _redis.GetDatabase();
            return await db.StringSetAsync(key, value, expiration, when: When.NotExists);
        }

        // Fallback khi không có multiplexer (test tự new, hoặc host chưa đăng ký):
        // KHÔNG atomic — chấp nhận vì chỉ là đường dự phòng, và vẫn tốt hơn ném lỗi.
        _logger?.LogDebug(
            "TrySetIfNotExists dùng fallback không-atomic cho key {Key} (thiếu IConnectionMultiplexer).", key);

        var existing = await _cache.GetStringAsync(key, cancellationToken);
        if (!string.IsNullOrEmpty(existing))
            return false;

        await _cache.SetStringAsync(key, value,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = expiration }, cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<long> IncrementAsync(
        string key, TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        if (_redis is not null)
        {
            var db = _redis.GetDatabase();
            var value = await db.StringIncrementAsync(key);

            // Chỉ đặt TTL ở lần tạo đầu tiên. Đặt lại mỗi lần tăng sẽ đẩy cửa sổ lùi mãi và
            // biến rate limit thành vô hiệu với người dùng gửi liên tục.
            if (value == 1)
                await db.KeyExpireAsync(key, expiration);

            return value;
        }

        // Fallback khi không có multiplexer — KHÔNG atomic. Chấp nhận vì chỉ là đường dự phòng
        // (test tự new, host chưa đăng ký Redis); rate limit khi đó chỉ gần đúng.
        _logger?.LogDebug(
            "Increment dùng fallback không-atomic cho key {Key} (thiếu IConnectionMultiplexer).", key);

        var current = await _cache.GetStringAsync(key, cancellationToken);
        var next = long.TryParse(current, out var parsed) ? parsed + 1 : 1;

        await _cache.SetStringAsync(key, next.ToString(),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = expiration }, cancellationToken);

        return next;
    }

    /// <inheritdoc />
    public async Task<long?> GetCounterAsync(
        string key, CancellationToken cancellationToken = default)
    {
        string? value;

        if (_redis is not null)
        {
            var redisValue = await _redis.GetDatabase().StringGetAsync(key);
            value = redisValue.HasValue ? redisValue.ToString() : null;
        }
        else
        {
            value = await _cache.GetStringAsync(key, cancellationToken);
        }

        return long.TryParse(value, out var counter) ? counter : null;
    }

    public async Task<bool> TryRefreshLeaseAsync(
        string key, string ownerToken, TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        if (_redis is not null)
        {
            var result = await _redis.GetDatabase().ScriptEvaluateAsync(
                CompareAndExpireScript,
                new RedisKey[] { key },
                new RedisValue[] { ownerToken, (long)expiration.TotalMilliseconds });
            return (long)result > 0;
        }

        var existing = await _cache.GetStringAsync(key, cancellationToken);
        if (!string.Equals(existing, ownerToken, StringComparison.Ordinal))
            return false;

        await _cache.SetStringAsync(key, ownerToken,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = expiration }, cancellationToken);
        return true;
    }

    public async Task<bool> TryReleaseLeaseAsync(
        string key, string ownerToken, CancellationToken cancellationToken = default)
    {
        if (_redis is not null)
        {
            var result = await _redis.GetDatabase().ScriptEvaluateAsync(
                CompareAndDeleteScript,
                new RedisKey[] { key },
                new RedisValue[] { ownerToken });
            return (long)result > 0;
        }

        var existing = await _cache.GetStringAsync(key, cancellationToken);
        if (!string.Equals(existing, ownerToken, StringComparison.Ordinal))
            return false;

        await _cache.RemoveAsync(key, cancellationToken);
        return true;
    }
}
