using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace SharedInfrastructure.Idempotency;

public class RedisIdempotencyKeyStore : IIdempotencyKeyStore
{
    private const string FieldStatusCode = "statusCode";
    private const string FieldBody = "body";
    private const string FieldStatus = "status";
    private const string StatusProcessing = "processing";
    private const string StatusDone = "done";

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisIdempotencyKeyStore> _logger;

    public RedisIdempotencyKeyStore(IConnectionMultiplexer redis, ILogger<RedisIdempotencyKeyStore> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<bool> TryReserveAsync(string key, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        var redisKey = BuildKey(key);
        var db = _redis.GetDatabase();

        // Atomic HSETNX trên field "status" để giữ ô. Trả false nếu key đã tồn tại.
        var ok = await db.HashSetAsync(redisKey, FieldStatus, StatusProcessing, When.NotExists);
        if (!ok)
            return false;

        await db.KeyExpireAsync(redisKey, ttl);
        return true;
    }

    public async Task SaveResponseAsync(string key, int statusCode, string body, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        var redisKey = BuildKey(key);
        var db = _redis.GetDatabase();

        await db.HashSetAsync(redisKey, new[]
        {
            new HashEntry(FieldStatus, StatusDone),
            new HashEntry(FieldStatusCode, statusCode),
            new HashEntry(FieldBody, body)
        });
        await db.KeyExpireAsync(redisKey, ttl);
    }

    public async Task<CachedIdempotencyResponse?> TryGetResponseAsync(string key, CancellationToken cancellationToken = default)
    {
        var redisKey = BuildKey(key);
        var db = _redis.GetDatabase();

        var entries = await db.HashGetAllAsync(redisKey);
        if (entries.Length == 0)
            return null;

        string? status = null;
        int? statusCode = null;
        string? body = null;
        foreach (var e in entries)
        {
            if (e.Name == FieldStatus)
                status = e.Value;
            else if (e.Name == FieldStatusCode)
                statusCode = (int)e.Value;
            else if (e.Name == FieldBody)
                body = e.Value;
        }

        if (status == StatusDone && statusCode.HasValue && body != null)
            return new CachedIdempotencyResponse(statusCode.Value, body);

        return null;
    }

    private static string BuildKey(string key) => $"idempotency:{key}";
}
