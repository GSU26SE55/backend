using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace SharedInfrastructure.Idempotency;

/// <summary>
/// Inbox trên Redis — xem <see cref="IInboxStore"/> cho vòng đời ba bước (GH-764).
/// </summary>
/// <remarks>
/// Một khoá duy nhất cho mỗi (message, consumer), giá trị nói rõ đang ở bước nào:
/// <list type="bullet">
///   <item><c>p:{token}</c> — có người đang xử lý, kèm dấu sở hữu. TTL ngắn (chỗ giữ).</item>
///   <item><c>d</c> — đã xong. TTL dài (chống trùng thật sự).</item>
/// </list>
/// Chốt và nhả đều chạy qua script Lua so dấu sở hữu, để một tiến trình có chỗ giữ ĐÃ HẾT HẠN
/// không xoá hay ghi đè lên lượt xử lý của người khác.
/// </remarks>
public class RedisInboxStore : IInboxStore
{
    private const string InProgressPrefix = "p:";
    private const string CompletedValue = "d";

    /// <summary>Xoá khoá chỉ khi nó vẫn là chỗ giữ CỦA TA.</summary>
    private const string ReleaseScript = @"
if redis.call('GET', KEYS[1]) == ARGV[1] then
    return redis.call('DEL', KEYS[1])
end
return 0";

    /// <summary>Chuyển sang 'đã xong' chỉ khi chỗ giữ vẫn là của ta.</summary>
    private const string CompleteScript = @"
if redis.call('GET', KEYS[1]) == ARGV[1] then
    redis.call('SET', KEYS[1], ARGV[2], 'EX', ARGV[3])
    return 1
end
return 0";

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

    private static string Key(Guid messageId, string consumerName) => $"inbox:{consumerName}:{messageId:N}";

    public async Task<InboxClaim> TryBeginAsync(
        Guid messageId, string consumerName, CancellationToken cancellationToken = default)
    {
        var key = Key(messageId, consumerName);
        var token = InProgressPrefix + Guid.NewGuid().ToString("N");
        var lease = TimeSpan.FromSeconds(Math.Max(1, _options.LeaseSeconds));

        try
        {
            var db = _redis.GetDatabase();
            if (await db.StringSetAsync(key, token, lease, when: When.NotExists))
                return new InboxClaim(InboxClaimStatus.Claimed, token);

            // Không giành được ⇒ đọc xem khoá đang ở bước nào.
            var current = await db.StringGetAsync(key);
            if (!current.HasValue)
            {
                // Khoá vừa hết hạn hoặc vừa được nhả ngay giữa hai lệnh. Thử lại đúng một lần:
                // lặp vô hạn ở đây sẽ biến một cuộc đua hiếm thành vòng quay bận.
                if (await db.StringSetAsync(key, token, lease, when: When.NotExists))
                    return new InboxClaim(InboxClaimStatus.Claimed, token);
                return InboxClaim.Busy;
            }

            return current == CompletedValue ? InboxClaim.Completed : InboxClaim.Busy;
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Redis Inbox check failed for {Key}. FailOpen={FailOpen}", key, _options.FailOpenWhenRedisDown);
            if (_options.FailOpenWhenRedisDown)
            {
                // Chấp nhận có thể xử lý lặp còn hơn dừng hẳn việc tiêu thụ message. Dấu sở hữu
                // rỗng ⇒ chốt/nhả sau đó thành vô hiệu, đúng với việc ta chẳng giữ chỗ nào cả.
                return new InboxClaim(InboxClaimStatus.Claimed, string.Empty);
            }
            throw;
        }
    }

    public async Task CompleteAsync(
        Guid messageId, string consumerName, string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(token))
            return;   // chế độ fail-open: không có chỗ giữ nào để chốt.

        var key = Key(messageId, consumerName);
        var ttlSeconds = (long)TimeSpan.FromDays(Math.Max(1, _options.TtlDays)).TotalSeconds;

        try
        {
            var db = _redis.GetDatabase();
            var changed = (int)await db.ScriptEvaluateAsync(
                CompleteScript,
                new RedisKey[] { key },
                new RedisValue[] { token, CompletedValue, ttlSeconds });

            if (changed == 0)
            {
                // Chỗ giữ đã hết hạn và người khác đã nhận lại message. Side effect của ta VẪN
                // đã chạy, nên nhiều khả năng nó chạy hai lần — nói ra để còn lần được, thay vì
                // âm thầm ghi đè trạng thái của người đang xử lý.
                _logger.LogWarning(
                    "Inbox lease expired before completion for {Key} (consumer {Consumer}). "
                    + "Side effect có thể đã chạy hơn một lần — cân nhắc tăng Inbox:LeaseSeconds.",
                    key, consumerName);
            }
        }
        catch (RedisException ex)
        {
            // Không chốt được thì chỗ giữ sẽ tự hết hạn và message có thể được xử lý lại. Ném ra
            // ở đây sẽ biến một side effect ĐÃ THÀNH CÔNG thành thất bại — tệ hơn hẳn.
            _logger.LogWarning(ex, "Redis Inbox complete failed for {Key} — chỗ giữ sẽ tự hết hạn.", key);
        }
    }

    public async Task ReleaseAsync(
        Guid messageId, string consumerName, string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(token))
            return;

        var key = Key(messageId, consumerName);
        try
        {
            var db = _redis.GetDatabase();
            await db.ScriptEvaluateAsync(ReleaseScript, new RedisKey[] { key }, new RedisValue[] { token });
        }
        catch (RedisException ex)
        {
            // Nhả hụt thì chỗ giữ tự hết hạn sau LeaseSeconds — chậm hơn, nhưng vẫn xử lý lại được.
            _logger.LogWarning(ex, "Redis Inbox release failed for {Key} — chờ chỗ giữ hết hạn.", key);
        }
    }
}
