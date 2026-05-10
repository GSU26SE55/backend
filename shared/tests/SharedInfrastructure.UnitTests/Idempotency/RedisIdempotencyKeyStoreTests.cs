using Microsoft.Extensions.Logging.Abstractions;
using SharedInfrastructure.Idempotency;
using StackExchange.Redis;

namespace SharedInfrastructure.UnitTests.Idempotency;

public class RedisIdempotencyKeyStoreTests
{
    private readonly Mock<IConnectionMultiplexer> _redisMock = new();
    private readonly Mock<IDatabase> _dbMock = new();

    public RedisIdempotencyKeyStoreTests()
    {
        _redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
                  .Returns(_dbMock.Object);
    }

    private RedisIdempotencyKeyStore CreateStore() =>
        new(_redisMock.Object, NullLogger<RedisIdempotencyKeyStore>.Instance);

    [Fact]
    public async Task TryReserveAsync_FirstCall_UsesHsetnx_ReturnsTrue()
    {
        _dbMock.Setup(d => d.HashSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<RedisValue>(),
                When.NotExists, CommandFlags.None))
            .ReturnsAsync(true);

        _dbMock.Setup(d => d.KeyExpireAsync(It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(), ExpireWhen.Always, CommandFlags.None))
            .ReturnsAsync(true);

        var store = CreateStore();

        var ok = await store.TryReserveAsync("key-123", TimeSpan.FromHours(24));

        ok.Should().BeTrue();
        _dbMock.Verify(d => d.HashSetAsync(
            It.Is<RedisKey>(k => ((string)k!) == "idempotency:key-123"),
            It.Is<RedisValue>(v => ((string)v!) == "status"),
            It.Is<RedisValue>(v => ((string)v!) == "processing"),
            When.NotExists,
            CommandFlags.None), Times.Once);
        _dbMock.Verify(d => d.KeyExpireAsync(
            It.Is<RedisKey>(k => ((string)k!) == "idempotency:key-123"),
            It.IsAny<TimeSpan?>(), ExpireWhen.Always, CommandFlags.None), Times.Once);
    }

    [Fact]
    public async Task TryReserveAsync_KeyExists_ReturnsFalse_NoExpire()
    {
        _dbMock.Setup(d => d.HashSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<RedisValue>(),
                When.NotExists, CommandFlags.None))
            .ReturnsAsync(false);

        var store = CreateStore();

        var ok = await store.TryReserveAsync("dup", TimeSpan.FromHours(24));

        ok.Should().BeFalse();
        _dbMock.Verify(d => d.KeyExpireAsync(It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(), ExpireWhen.Always, CommandFlags.None), Times.Never);
    }

    [Fact]
    public async Task SaveResponseAsync_WritesAllFields_AndExtendsTtl()
    {
        _dbMock.Setup(d => d.HashSetAsync(It.IsAny<RedisKey>(), It.IsAny<HashEntry[]>(), CommandFlags.None))
               .Returns(Task.CompletedTask);
        _dbMock.Setup(d => d.KeyExpireAsync(It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(), ExpireWhen.Always, CommandFlags.None))
               .ReturnsAsync(true);

        var store = CreateStore();

        await store.SaveResponseAsync("k", 200, "{\"ok\":true}", TimeSpan.FromHours(24));

        _dbMock.Verify(d => d.HashSetAsync(
            It.Is<RedisKey>(k => ((string)k!) == "idempotency:k"),
            It.Is<HashEntry[]>(arr =>
                arr.Any(e => e.Name == "status" && e.Value == "done") &&
                arr.Any(e => e.Name == "statusCode" && (int)e.Value == 200) &&
                arr.Any(e => e.Name == "body" && e.Value == "{\"ok\":true}")),
            CommandFlags.None), Times.Once);
    }

    [Fact]
    public async Task TryGetResponseAsync_NoKey_ReturnsNull()
    {
        _dbMock.Setup(d => d.HashGetAllAsync(It.IsAny<RedisKey>(), CommandFlags.None))
               .ReturnsAsync(Array.Empty<HashEntry>());

        var store = CreateStore();

        var result = await store.TryGetResponseAsync("missing");

        result.Should().BeNull();
    }

    [Fact]
    public async Task TryGetResponseAsync_StatusProcessing_ReturnsNull()
    {
        _dbMock.Setup(d => d.HashGetAllAsync(It.IsAny<RedisKey>(), CommandFlags.None))
               .ReturnsAsync(new[] { new HashEntry("status", "processing") });

        var store = CreateStore();

        var result = await store.TryGetResponseAsync("k");

        result.Should().BeNull();  // Reserved nhưng chưa có response
    }

    [Fact]
    public async Task TryGetResponseAsync_StatusDone_ReturnsCachedTuple()
    {
        _dbMock.Setup(d => d.HashGetAllAsync(It.IsAny<RedisKey>(), CommandFlags.None))
               .ReturnsAsync(new[]
               {
                   new HashEntry("status", "done"),
                   new HashEntry("statusCode", 201),
                   new HashEntry("body", "{\"id\":\"abc\"}")
               });

        var store = CreateStore();

        var result = await store.TryGetResponseAsync("k");

        result.Should().NotBeNull();
        result!.StatusCode.Should().Be(201);
        result.Body.Should().Be("{\"id\":\"abc\"}");
    }
}
