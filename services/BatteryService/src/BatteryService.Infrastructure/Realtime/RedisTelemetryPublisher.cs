using System.Text.Json;
using BatteryService.Application.Common.Models;
using BatteryService.Application.DTOs.Realtime;
using BatteryService.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace BatteryService.Infrastructure.Realtime;

/// <summary>
/// Sprint BE-IoT-Realtime (#615/#616) — <see cref="ITelemetryPublisher"/> dùng Redis pub/sub.
/// Fan-out mỗi reading tới <c>telemetry:asset:{id}</c> + <c>telemetry:customer:{id}</c> + (nếu có) <c>telemetry:site:{id}</c>.
/// Soft-dependency: lỗi Redis chỉ log, KHÔNG ném ra (ingest không được chặn). No-op khi Realtime:Enabled=false.
/// </summary>
public class RedisTelemetryPublisher : ITelemetryPublisher
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IConnectionMultiplexer _redis;
    private readonly RealtimeOptions _options;
    private readonly ILogger<RedisTelemetryPublisher> _logger;

    public RedisTelemetryPublisher(
        IConnectionMultiplexer redis,
        IOptions<RealtimeOptions> options,
        ILogger<RedisTelemetryPublisher> logger)
    {
        _redis = redis;
        _options = options.Value;
        _logger = logger;
    }

    public async Task PublishAsync(IReadOnlyList<LiveReadingDto> readings, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || readings.Count == 0)
            return;

        try
        {
            var sub = _redis.GetSubscriber();
            foreach (var reading in readings)
            {
                var payload = JsonSerializer.Serialize(reading, JsonOptions);
                // Fan-out mọi chiều nhóm để scope nào cũng subscribe được (§34.10.5).
                await sub.PublishAsync(RedisChannel.Literal(RedisTelemetryChannels.Asset(reading.BatteryAssetId)), payload);
                await sub.PublishAsync(RedisChannel.Literal(RedisTelemetryChannels.Customer(reading.CustomerId)), payload);
                await sub.PublishAsync(
                    RedisChannel.Literal(reading.SiteId.HasValue
                        ? RedisTelemetryChannels.Site(reading.SiteId.Value)
                        : RedisTelemetryChannels.SiteNone),
                    payload);
                if (reading.BatteryTypeId.HasValue)
                    await sub.PublishAsync(RedisChannel.Literal(RedisTelemetryChannels.Type(reading.BatteryTypeId.Value)), payload);
                await sub.PublishAsync(RedisChannel.Literal(RedisTelemetryChannels.All), payload);
            }
        }
        catch (Exception ex)
        {
            // Soft-dependency — KHÔNG ném lỗi vào ingest handler.
            _logger.LogWarning(ex, "RedisTelemetryPublisher publish thất bại — bỏ qua ({Count} readings).", readings.Count);
        }
    }
}
