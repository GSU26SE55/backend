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
}
