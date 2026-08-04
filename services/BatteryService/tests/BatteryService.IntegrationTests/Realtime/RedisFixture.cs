using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using StackExchange.Redis;

namespace BatteryService.IntegrationTests.Realtime;

/// <summary>
/// Sprint BE-IoT-Realtime <c>#623</c> — Redis THẬT cho test SSE telemetry.
///
/// Đường đi thật của telemetry là: ingest → <c>RedisTelemetryPublisher</c> → <b>Redis pub/sub</b> →
/// <c>RedisTelemetryStream</c> → SSE. Redis chính là backplane cho fan-out nhiều instance
/// (<c>BEIOT-RT-02</c>), nên mock nó đi là mất đúng phần đáng kiểm nhất.
/// </summary>
public sealed class RedisFixture : IAsyncLifetime
{
    private IContainer _container = null!;

    public IConnectionMultiplexer Redis { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _container = new ContainerBuilder()
            .WithImage("redis:7-alpine")
            .WithPortBinding(6379, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(6379))
            .Build();

        await _container.StartAsync();

        Redis = await ConnectionMultiplexer.ConnectAsync(
            $"{_container.Hostname}:{_container.GetMappedPublicPort(6379)}");
    }

    public async Task DisposeAsync()
    {
        if (Redis is not null)
            await Redis.CloseAsync();
        if (_container is not null)
            await _container.DisposeAsync();
    }
}

[CollectionDefinition(nameof(RedisCollection))]
public sealed class RedisCollection : ICollectionFixture<RedisFixture>;
