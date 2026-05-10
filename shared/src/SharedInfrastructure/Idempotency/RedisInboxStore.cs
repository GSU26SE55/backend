using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace SharedInfrastructure.Idempotency;

public class RedisInboxStore : IInboxStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly InboxOptions _options;
    private readonly ILogger<RedisInboxStore> _logger;

    public RedisInboxStore(
        IConnectionMultiplexer redis,
        IOptions<InboxOptions> options,
        ILogger<RedisInboxStore> logger)
    {
        _redis = redis;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> TryMarkProcessedAsync(Guid messageId, string consumerName, CancellationToken cancellationToken = default)
    {
        var key = $"inbox:{consumerName}:{messageId:N}";
        var ttl = TimeSpan.FromDays(_options.TtlDays);

        try
        {
            var db = _redis.GetDatabase();
            var firstTime = await db.StringSetAsync(key, "1", ttl, when: When.NotExists);
            return firstTime;
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Redis Inbox check failed for {Key}. FailOpen={FailOpen}", key, _options.FailOpenWhenRedisDown);
            if (_options.FailOpenWhenRedisDown)
            {
                return true;
            }
            throw;
        }
    }
}
