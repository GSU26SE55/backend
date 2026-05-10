using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharedInfrastructure.Idempotency;
using StackExchange.Redis;

namespace SharedInfrastructure.UnitTests.Idempotency;

public class RedisInboxStoreTests
{
    private readonly Mock<IConnectionMultiplexer> _redisMock = new();
    private readonly Mock<IDatabase> _dbMock = new();

    public RedisInboxStoreTests()
    {
        _redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
                  .Returns(_dbMock.Object);
    }

    private RedisInboxStore CreateStore(InboxOptions? options = null) =>
        new(_redisMock.Object,
            Options.Create(options ?? new InboxOptions { TtlDays = 7, FailOpenWhenRedisDown = false }),
            NullLogger<RedisInboxStore>.Instance);

    /// <summary>
    /// IDatabase.StringSetAsync có nhiều overload. Compiler resolve lựa chọn dựa trên args
    /// production: `db.StringSetAsync(key, "1", ttl, when: When.NotExists)`.
    /// Setup loose với It.IsAny để match bất kỳ overload nào Moq đang track.
    /// </summary>
    /// <summary>
    /// IDatabaseAsync.StringSetAsync overload 4-arg: (RedisKey, RedisValue, TimeSpan?, When).
    /// Compiler resolve khi gọi `db.StringSetAsync(key, "1", ttl, when: When.NotExists)`.
    /// Confirmed via MockBehavior.Strict trace.
    /// </summary>
    [Fact]
    public async Task TryMarkProcessedAsync_FirstCall_ReturnsTrue()
    {
        _dbMock.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), When.NotExists))
            .ReturnsAsync(true);

        var store = CreateStore();

        var result = await store.TryMarkProcessedAsync(Guid.NewGuid(), "MyConsumer");

        result.Should().BeTrue();
        _dbMock.Verify(d => d.StringSetAsync(
            It.Is<RedisKey>(k => ((string)k!).StartsWith("inbox:MyConsumer:")),
            It.IsAny<RedisValue>(),
            It.Is<TimeSpan?>(t => t == TimeSpan.FromDays(7)),
            When.NotExists), Times.Once);
    }

    [Fact]
    public async Task TryMarkProcessedAsync_DuplicateCall_ReturnsFalse()
    {
        _dbMock.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), When.NotExists))
            .ReturnsAsync(false);

        var store = CreateStore();

        var result = await store.TryMarkProcessedAsync(Guid.NewGuid(), "MyConsumer");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task RedisDown_FailOpenTrue_ReturnsTrue_AndDoesNotThrow()
    {
        _dbMock.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), When.NotExists))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));

        var store = CreateStore(new InboxOptions { TtlDays = 7, FailOpenWhenRedisDown = true });

        var result = await store.TryMarkProcessedAsync(Guid.NewGuid(), "C");
        result.Should().BeTrue();
    }

    [Fact]
    public async Task RedisDown_FailOpenFalse_Throws()
    {
        _dbMock.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), When.NotExists))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));

        var store = CreateStore(new InboxOptions { TtlDays = 7, FailOpenWhenRedisDown = false });

        var act = async () => await store.TryMarkProcessedAsync(Guid.NewGuid(), "C");

        await act.Should().ThrowAsync<RedisConnectionException>();
    }

    [Fact]
    public async Task KeyFormat_Contains_ConsumerName_And_MessageId()
    {
        RedisKey capturedKey = default;
        _dbMock.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), When.NotExists))
            .Callback<RedisKey, RedisValue, TimeSpan?, When>(
                (k, _, _, _) => capturedKey = k)
            .ReturnsAsync(true);

        var store = CreateStore();
        var msgId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        await store.TryMarkProcessedAsync(msgId, "SendOtp");

        ((string)capturedKey!).Should().Be($"inbox:SendOtp:{msgId:N}");
    }
}
