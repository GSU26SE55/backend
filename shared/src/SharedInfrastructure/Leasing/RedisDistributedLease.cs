using StackExchange.Redis;

namespace SharedInfrastructure.Leasing;

/// <summary>
/// GH-793 — hiện thực <see cref="IDistributedLease"/> trên Redis.
/// </summary>
/// <remarks>
/// <para>
/// Mọi phép đều là MỘT lệnh phía Redis (script Lua chạy nguyên khối), nên không có khe hở giữa
/// "đọc" và "ghi" như khuôn <c>GET</c> rồi <c>SET</c> cũ.
/// </para>
/// <para>
/// Không nuốt lỗi kết nối: quyết định "Redis hỏng thì làm gì" thuộc về nơi gọi, vì mỗi công việc
/// nền chịu đựng khác nhau. Nuốt ở đây sẽ biến sự cố hạ tầng thành "không ai là chủ" một cách âm thầm.
/// </para>
/// </remarks>
public sealed class RedisDistributedLease : IDistributedLease
{
    /// <summary>
    /// Giành quyền nếu khoá trống, HOẶC gia hạn nếu chính ta đang giữ.
    /// </summary>
    /// <remarks>
    /// Gộp hai việc vào một script vì công việc nền gọi lại mỗi nhịp: tách ra thì giữa lần kiểm và
    /// lần ghi vẫn còn khe hở, đúng cái khe đã sinh ra lỗi này.
    /// </remarks>
    private const string AcquireScript = @"
if redis.call('SET', KEYS[1], ARGV[1], 'NX', 'PX', ARGV[2]) then
    return 1
end
if redis.call('GET', KEYS[1]) == ARGV[1] then
    redis.call('PEXPIRE', KEYS[1], ARGV[2])
    return 1
end
return 0";

    /// <summary>Gia hạn CHỈ KHI ta vẫn là chủ — instance đã mất quyền không được giành lại lén.</summary>
    private const string RenewScript = @"
if redis.call('GET', KEYS[1]) == ARGV[1] then
    redis.call('PEXPIRE', KEYS[1], ARGV[2])
    return 1
end
return 0";

    /// <summary>Nhả CHỈ KHI ta vẫn là chủ — nếu không sẽ xoá mất quyền của chủ mới.</summary>
    private const string ReleaseScript = @"
if redis.call('GET', KEYS[1]) == ARGV[1] then
    return redis.call('DEL', KEYS[1])
end
return 0";

    private readonly IConnectionMultiplexer _redis;

    public RedisDistributedLease(IConnectionMultiplexer redis) => _redis = redis;

    public async Task<bool> TryAcquireAsync(string key, string owner, TimeSpan ttl, CancellationToken ct = default)
    {
        Validate(key, owner, ttl);

        var result = await _redis.GetDatabase().ScriptEvaluateAsync(
            AcquireScript, [key], [owner, (long)ttl.TotalMilliseconds]);

        return (long)result == 1;
    }

    public async Task<bool> TryRenewAsync(string key, string owner, TimeSpan ttl, CancellationToken ct = default)
    {
        Validate(key, owner, ttl);

        var result = await _redis.GetDatabase().ScriptEvaluateAsync(
            RenewScript, [key], [owner, (long)ttl.TotalMilliseconds]);

        return (long)result == 1;
    }

    public async Task ReleaseAsync(string key, string owner, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        await _redis.GetDatabase().ScriptEvaluateAsync(ReleaseScript, [key], [owner]);
    }

    private static void Validate(string key, string owner, TimeSpan ttl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        // Chủ sở hữu rỗng làm mọi instance trông giống nhau: ai cũng gia hạn và nhả được quyền của
        // người khác, tức là mất hẳn tác dụng của việc đối chiếu.
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        if (ttl <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ttl), ttl, "Duration must be positive.");
    }
}
