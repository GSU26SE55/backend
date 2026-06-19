using Moq;
using StackExchange.Redis;

namespace AuthService.UnitTests.Helpers;

/// <summary>
/// Stub IConnectionMultiplexer trả về IDatabase mock với behavior default an toàn cho test:
/// - StringIncrement: always returns 1 (rate limit chưa trigger).
/// - StringSet NotExists: always returns true (single-use chưa consumed).
/// - StringGet: returns RedisValue.Null.
/// - KeyDelete/KeyExpire: no-op.
/// Tests cần behavior khác có thể truyền callbacks tùy chỉnh.
/// </summary>
public static class StubRedis
{
    public static Mock<IConnectionMultiplexer> Build(
        long incrementValue = 1,
        bool setNxResult = true)
    {
        var db = new Mock<IDatabase>(MockBehavior.Loose);

        db.Setup(d => d.StringIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(incrementValue);

        db.Setup(d => d.KeyExpireAsync(It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        db.Setup(d => d.KeyExpireAsync(It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(), It.IsAny<ExpireWhen>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Cover ALL 3 StringSetAsync overloads (StackExchange.Redis 2.9):
        // (RedisKey, RedisValue, TimeSpan?, When)
        // (RedisKey, RedisValue, TimeSpan?, When, CommandFlags)
        // (RedisKey, RedisValue, TimeSpan?, bool, When, CommandFlags)
        db.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<When>()))
            .ReturnsAsync(setNxResult);
        db.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(setNxResult);
        db.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(setNxResult);

        db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        db.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Set member (cho 2FA challenge store).
        db.Setup(d => d.SetAddAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var redis = new Mock<IConnectionMultiplexer>(MockBehavior.Loose);
        redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(db.Object);
        // Cover parameterless overload too (in case compiler resolves to it directly).
        redis.Setup(r => r.GetDatabase(-1, null)).Returns(db.Object);
        return redis;
    }
}
