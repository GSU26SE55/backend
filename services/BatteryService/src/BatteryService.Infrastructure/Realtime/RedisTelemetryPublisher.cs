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
            var db = _redis.GetDatabase();
            foreach (var reading in readings)
            {
                var payload = JsonSerializer.Serialize(reading, JsonOptions);

                // ─── Replay buffer cho scope 1 pin (#614 Last-Event-ID) ───
                // Ghi vào Redis Stream TRƯỚC khi publish để lấy id; id này đi kèm message pub/sub và
                // được controller ghi ra dòng `id:`. Nhờ vậy client reconnect gửi lại `Last-Event-ID`
                // là server biết phát bù từ đâu.
                //
                // Chỉ làm cho kênh asset: đây là scope duy nhất phát event `reading` từng bản ghi.
                // Các scope gộp (customer/site/type/all) phát `summary` — ảnh chụp định kỳ, bỏ lỡ
                // vài nhịp không mất dữ liệu nên không cần replay.
                var replayId = await AppendReplayAsync(db, reading, payload);

                // Fan-out mọi chiều nhóm để scope nào cũng subscribe được (§34.10.5).
                // Kênh asset mang thêm tiền tố id; các kênh khác giữ nguyên payload thuần.
                await sub.PublishAsync(
                    RedisChannel.Literal(RedisTelemetryChannels.Asset(reading.BatteryAssetId)),
                    replayId is null ? payload : $"{replayId} {payload}");
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

    /// <summary>
    /// <c>XADD</c> reading vào stream replay của pin, trả về id do Redis sinh (<c>&lt;ms&gt;-&lt;seq&gt;</c>).
    /// Trả <c>null</c> nếu tắt replay hoặc ghi hỏng — khi đó message vẫn publish bình thường, chỉ là
    /// không có <c>id:</c> để resume (mất tính năng bù, KHÔNG mất luồng realtime).
    /// </summary>
    private async Task<string?> AppendReplayAsync(IDatabase db, LiveReadingDto reading, string payload)
    {
        if (_options.ReplayMaxEvents <= 0)
            return null;

        try
        {
            var key = (RedisKey)RedisTelemetryChannels.AssetReplay(reading.BatteryAssetId);
            // maxLength + useApproximateMaxLength: Redis cắt tại biên node radix — rẻ hơn cắt chính xác.
            var id = await db.StreamAddAsync(
                key, ReplayPayloadField, payload,
                maxLength: _options.ReplayMaxEvents, useApproximateMaxLength: true);

            if (_options.ReplayTtlMinutes > 0)
                await db.KeyExpireAsync(key, TimeSpan.FromMinutes(_options.ReplayTtlMinutes));

            return id.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ghi replay stream thất bại cho pin {AssetId} — bỏ qua Last-Event-ID.",
                reading.BatteryAssetId);
            return null;
        }
    }

    /// <summary>Tên field trong entry của Redis Stream — dùng chung với phía đọc lại.</summary>
    internal const string ReplayPayloadField = "d";
}
