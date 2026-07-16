using BatteryService.Application.Common.Models;
using BatteryService.Application.DTOs.Realtime;
using BatteryService.Infrastructure.Realtime;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace BatteryService.UnitTests.Infrastructure;

/// <summary>
/// Sprint Bonus NS-03/NS-04 (#648/#649) — hành vi soft-dependency của stats service:
/// no-op khi Realtime:Enabled=false / readings rỗng; Redis down → KHÔNG ném ra ingest.
/// (Merge logic thuần test ở TelemetryStatsMathTests; Lua trên Redis thật là integration.)
/// </summary>
public class RedisTelemetryStatsServiceTests
{
    private static RedisTelemetryStatsService Sut(IConnectionMultiplexer redis, bool enabled = true) =>
        new(redis,
            Options.Create(new RealtimeOptions { Enabled = enabled }),
            NullLogger<RedisTelemetryStatsService>.Instance);

    private static LiveReadingDto Reading(decimal current) => new()
    {
        BatteryAssetId = Guid.NewGuid(),
        CustomerId = Guid.NewGuid(),
        Time = new DateTime(2026, 7, 8, 9, 0, 0, DateTimeKind.Utc),
        Current = current,
        SensorSourceCode = "primary"
    };

    [Fact]
    public async Task Disabled_NoOp_NoRedisInteraction()
    {
        var redis = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);

        await Sut(redis.Object, enabled: false)
            .AccumulateAndPublishAsync(new[] { Reading(2m) });

        redis.Verify(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()), Times.Never);
        redis.Verify(r => r.GetSubscriber(It.IsAny<object>()), Times.Never);
    }

    [Fact]
    public async Task EmptyReadings_NoOp()
    {
        var redis = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);

        var act = async () => await Sut(redis.Object)
            .AccumulateAndPublishAsync(Array.Empty<LiveReadingDto>());

        await act.Should().NotThrowAsync();
        redis.Verify(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()), Times.Never);
    }

    [Fact]
    public async Task RedisDown_DoesNotThrow()
    {
        var redis = new Mock<IConnectionMultiplexer>();
        redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Throws(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));

        var act = async () => await Sut(redis.Object)
            .AccumulateAndPublishAsync(new[] { Reading(2m) });

        await act.Should().NotThrowAsync("stats là soft-dependency, lỗi Redis không được chặn ingest");
    }
}
