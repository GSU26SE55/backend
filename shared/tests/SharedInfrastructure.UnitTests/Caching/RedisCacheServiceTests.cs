using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using SharedInfrastructure.Caching;

namespace SharedInfrastructure.UnitTests.Caching;

public class RedisCacheServiceTests
{
    private readonly Mock<IDistributedCache> _cache = new();
    private RedisCacheService Sut() => new(_cache.Object);

    private record User(Guid Id, string Name);

    [Fact]
    public async Task GetAsync_NoData_ReturnsDefault()
    {
        _cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((byte[]?)null);

        var result = await Sut().GetAsync<User>("missing");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_EmptyString_ReturnsDefault()
    {
        _cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Array.Empty<byte>());

        var result = await Sut().GetAsync<User>("empty");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_HasJson_DeserializesCorrectly()
    {
        var user = new User(Guid.NewGuid(), "alice");
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(user));
        _cache.Setup(c => c.GetAsync("u1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(bytes);

        var result = await Sut().GetAsync<User>("u1");

        result.Should().NotBeNull();
        result!.Id.Should().Be(user.Id);
        result.Name.Should().Be("alice");
    }

    [Fact]
    public async Task SetAsync_DefaultExpiration_Uses10Minutes()
    {
        DistributedCacheEntryOptions? captured = null;
        _cache.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
              .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>((_, _, opts, _) => captured = opts)
              .Returns(Task.CompletedTask);

        await Sut().SetAsync("k", new User(Guid.NewGuid(), "a"));

        captured.Should().NotBeNull();
        captured!.AbsoluteExpirationRelativeToNow.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public async Task SetAsync_CustomExpiration_UsesProvidedTimeSpan()
    {
        DistributedCacheEntryOptions? captured = null;
        _cache.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
              .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>((_, _, opts, _) => captured = opts)
              .Returns(Task.CompletedTask);

        await Sut().SetAsync("k", "v", TimeSpan.FromSeconds(42));

        captured!.AbsoluteExpirationRelativeToNow.Should().Be(TimeSpan.FromSeconds(42));
    }

    [Fact]
    public async Task SetAsync_SerializesValueAsJson()
    {
        byte[]? capturedBytes = null;
        _cache.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
              .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>((_, b, _, _) => capturedBytes = b)
              .Returns(Task.CompletedTask);

        var u = new User(Guid.NewGuid(), "bob");
        await Sut().SetAsync("k", u);

        capturedBytes.Should().NotBeNull();
        var json = Encoding.UTF8.GetString(capturedBytes!);
        json.Should().Contain("bob");
    }

    [Fact]
    public async Task RemoveAsync_DelegatesToCache()
    {
        _cache.Setup(c => c.RemoveAsync("k", It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask)
              .Verifiable();

        await Sut().RemoveAsync("k");

        _cache.Verify();
    }

    // ════════ Sprint 6.3 NOTI3-09 (#709) — TrySetIfNotExistsAsync ════════
    //
    // Không có Redis thật trong unit test nên phần này kiểm nhánh FALLBACK (host chưa đăng ký
    // IConnectionMultiplexer). Nhánh atomic thật dùng StringSetAsync(..., When.NotExists) của
    // StackExchange.Redis — đúng API mà RedisInboxStore đã dùng và đã chạy production.

    /// <summary>IDistributedCache in-memory tối giản, đủ cho nhánh fallback.</summary>
    private sealed class FakeDistributedCache : IDistributedCache
    {
        private readonly Dictionary<string, byte[]> _store = new();

        public byte[]? Get(string key) => _store.TryGetValue(key, out var v) ? v : null;
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) => _store.Remove(key);
        public Task RemoveAsync(string key, CancellationToken token = default) { Remove(key); return Task.CompletedTask; }
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => _store[key] = value;
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        { Set(key, value, options); return Task.CompletedTask; }
    }

    private static RedisCacheService SutWithRealStore() => new(new FakeDistributedCache());

    [Fact]
    public async Task TrySetIfNotExists_FirstCall_ReturnsTrue()
    {
        (await SutWithRealStore().TrySetIfNotExistsAsync("k1", "v", TimeSpan.FromMinutes(5)))
            .Should().BeTrue();
    }

    [Fact]
    public async Task TrySetIfNotExists_SecondCall_ReturnsFalse_AndKeepsOriginalValue()
    {
        var sut = SutWithRealStore();

        await sut.TrySetIfNotExistsAsync("k1", "first", TimeSpan.FromMinutes(5));
        var second = await sut.TrySetIfNotExistsAsync("k1", "second", TimeSpan.FromMinutes(5));

        second.Should().BeFalse("chỉ lần đầu được chiếm key — đây là điều kiện để debounce đúng");
    }

    [Fact]
    public async Task TrySetIfNotExists_DifferentKeys_BothSucceed()
    {
        var sut = SutWithRealStore();

        (await sut.TrySetIfNotExistsAsync("a", "1", TimeSpan.FromMinutes(5))).Should().BeTrue();
        (await sut.TrySetIfNotExistsAsync("b", "1", TimeSpan.FromMinutes(5))).Should().BeTrue();
    }

    [Fact]
    public async Task TrySetIfNotExists_AfterRemove_CanBeClaimedAgain()
    {
        var sut = SutWithRealStore();

        await sut.TrySetIfNotExistsAsync("k", "1", TimeSpan.FromMinutes(5));
        await sut.RemoveAsync("k");

        (await sut.TrySetIfNotExistsAsync("k", "2", TimeSpan.FromMinutes(5)))
            .Should().BeTrue("hết TTL / bị xoá thì cửa sổ debounce mới được phép mở lại");
    }
}
