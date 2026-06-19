using Moq;
using StackExchange.Redis;

namespace AuthService.IntegrationTests.Fixtures;

/// <summary>
/// Builder dùng Moq để tạo IConnectionMultiplexer stub với behavior mặc định an toàn:
/// - StringSet (mọi overload) → true (single-use chưa consumed / SET NX acquire success).
/// - StringGet → RedisValue.Null.
/// - StringIncrement → 1.
/// - KeyExpire / KeyDelete / SetAdd → true.
///
/// Giữ MockBehavior.Loose → các method không setup return default(T) thay vì throw.
/// Phủ ~200 method IDatabase mà không cần implement thủ công.
/// </summary>
internal static class StubConnectionMultiplexer
{
    public static IConnectionMultiplexer Build()
    {
        var db = new Mock<IDatabase>(MockBehavior.Loose);

        // StringSet — 3 overload chính (StackExchange.Redis 2.9).
        db.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                It.IsAny<When>()))
            .ReturnsAsync(true);
        db.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        db.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        db.Setup(d => d.StringIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(1L);

        db.Setup(d => d.KeyExpireAsync(It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        db.Setup(d => d.KeyExpireAsync(It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(), It.IsAny<ExpireWhen>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        db.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        db.Setup(d => d.SetAddAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var mux = new Mock<IConnectionMultiplexer>(MockBehavior.Loose);
        mux.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(db.Object);
        mux.Setup(m => m.GetDatabase(-1, null)).Returns(db.Object);
        mux.SetupGet(m => m.IsConnected).Returns(true);
        mux.SetupGet(m => m.Configuration).Returns("stub");

        return mux.Object;
    }
}
