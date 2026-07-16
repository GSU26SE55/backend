using System.Globalization;
using System.Text.Json;
using BatteryService.Application.Common;
using BatteryService.Application.Common.Models;
using BatteryService.Application.DTOs.Realtime;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Realtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace BatteryService.Infrastructure.Realtime;

/// <summary>
/// Sprint Bonus NS-03/NS-04 (#648/#649) — <see cref="ITelemetryStatsService"/> dùng Redis.
/// Mỗi batch ingest: lọc reading primary → tách chiều nạp/xả → merge min/max vào HASH
/// <c>telemetry:stats:{asset}:{window}:{bucket}</c> (atomic bằng Lua) → publish event <c>stats</c>
/// lên kênh <c>telemetry:stats:*</c>. Soft-dependency: lỗi Redis chỉ log, KHÔNG ném ra ingest.
/// No-op khi <c>Realtime:Enabled=false</c>.
/// </summary>
public class RedisTelemetryStatsService : ITelemetryStatsService
{
    // Merge atomic min/max + cộng dồn count trong 1 round-trip (tránh race giữa 2 pod ingest).
    // ARGV: 1=maxCharge 2=minCharge 3=maxDischarge 4=minDischarge 5=chargeCount 6=dischargeCount 7=ttlSeconds.
    // Min/max rỗng ('') = batch không có mẫu chiều đó → không đụng field. Trả HMGET đã merge.
    private const string MergeScript = @"
local key = KEYS[1]
local function mergeExtreme(field, val, keepLarger)
  if val == '' then return end
  local cur = redis.call('HGET', key, field)
  if cur == false then
    redis.call('HSET', key, field, val)
  else
    local c = tonumber(cur)
    local v = tonumber(val)
    if (keepLarger and v > c) or ((not keepLarger) and v < c) then
      redis.call('HSET', key, field, val)
    end
  end
end
mergeExtreme('mc', ARGV[1], true)
mergeExtreme('nc', ARGV[2], false)
mergeExtreme('md', ARGV[3], true)
mergeExtreme('nd', ARGV[4], false)
if tonumber(ARGV[5]) > 0 then redis.call('HINCRBY', key, 'cc', ARGV[5]) end
if tonumber(ARGV[6]) > 0 then redis.call('HINCRBY', key, 'dc', ARGV[6]) end
redis.call('EXPIRE', key, ARGV[7])
return redis.call('HMGET', key, 'mc', 'nc', 'md', 'nd', 'cc', 'dc')
";

    private readonly IConnectionMultiplexer _redis;
    private readonly RealtimeOptions _options;
    private readonly ILogger<RedisTelemetryStatsService> _logger;

    public RedisTelemetryStatsService(
        IConnectionMultiplexer redis,
        IOptions<RealtimeOptions> options,
        ILogger<RedisTelemetryStatsService> logger)
    {
        _redis = redis;
        _options = options.Value;
        _logger = logger;
    }

    public async Task AccumulateAndPublishAsync(
        IReadOnlyList<LiveReadingDto> readings, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || readings.Count == 0)
            return;

        try
        {
            var now = DateTime.UtcNow;
            var db = _redis.GetDatabase();
            var sub = _redis.GetSubscriber();

            // Chỉ tính trên reading primary (cùng quy ước coalescer SSE) — tránh nhân 3 nguồn/pin.
            var byAsset = readings
                .Where(r => SensorSource.IsPrimary(r.SensorSourceCode))
                .GroupBy(r => r.BatteryAssetId);

            foreach (var assetGroup in byAsset)
            {
                var first = assetGroup.First();

                foreach (var window in StatsWindows.All)
                {
                    // Gom reading theo bucket window (an toàn khi batch vắt qua ranh giới giờ/ngày).
                    foreach (var bucket in assetGroup.GroupBy(r => StatsWindows.WindowStart(window, r.Time)))
                    {
                        var batch = TelemetryStatsMath.ComputeBatch(bucket.Select(r => r.Current));
                        if (!batch.HasSamples)
                            continue; // bucket toàn idle (current=0) → không có gì merge/publish

                        var windowStart = bucket.Key;
                        var merged = await MergeAsync(db, assetGroup.Key, window, windowStart, batch);

                        var dto = new LiveStatsDto
                        {
                            BatteryAssetId = assetGroup.Key,
                            CustomerId = first.CustomerId,
                            SiteId = first.SiteId,
                            Window = StatsWindows.Label(window),
                            WindowStart = windowStart,
                            MaxChargeCurrent = merged.MaxChargeCurrent,
                            MinChargeCurrent = merged.MinChargeCurrent,
                            MaxDischargeCurrent = merged.MaxDischargeCurrent,
                            MinDischargeCurrent = merged.MinDischargeCurrent,
                            ChargeSampleCount = merged.ChargeSampleCount,
                            DischargeSampleCount = merged.DischargeSampleCount,
                            UpdatedAt = now
                        };

                        await PublishAsync(sub, dto, first.BatteryTypeId);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Soft-dependency — KHÔNG ném lỗi vào ingest handler.
            _logger.LogWarning(ex, "RedisTelemetryStatsService accumulate/publish thất bại — bỏ qua ({Count} readings).", readings.Count);
        }
    }

    private async Task<DirectionalStats> MergeAsync(
        IDatabase db, Guid assetId, StatsWindowType window, DateTime windowStart, DirectionalStats batch)
    {
        var key = $"{RedisTelemetryChannels.StatsPrefix}:{assetId:N}:{StatsWindows.Label(window)}:{StatsWindows.KeySuffix(window, windowStart)}";
        var ttlSeconds = (long)StatsWindows.Ttl(window).TotalSeconds;

        var result = await db.ScriptEvaluateAsync(MergeScript,
            new RedisKey[] { key },
            new RedisValue[]
            {
                Num(batch.MaxChargeCurrent),
                Num(batch.MinChargeCurrent),
                Num(batch.MaxDischargeCurrent),
                Num(batch.MinDischargeCurrent),
                batch.ChargeSampleCount,
                batch.DischargeSampleCount,
                ttlSeconds
            });

        var values = (RedisValue[])result!;
        return new DirectionalStats(
            ParseNum(values[0]), ParseNum(values[1]), ParseNum(values[2]), ParseNum(values[3]),
            ParseInt(values[4]), ParseInt(values[5]));
    }

    private async Task PublishAsync(ISubscriber sub, LiveStatsDto dto, Guid? batteryTypeId)
    {
        var payload = JsonSerializer.Serialize(dto, RedisTelemetryPublisher.JsonOptions);

        // Fan-out song song đúng các chiều scope như publisher reading (asset/customer/site/type/all).
        await sub.PublishAsync(RedisChannel.Literal(RedisTelemetryChannels.StatsAsset(dto.BatteryAssetId)), payload);
        await sub.PublishAsync(RedisChannel.Literal(RedisTelemetryChannels.StatsCustomer(dto.CustomerId)), payload);
        await sub.PublishAsync(
            RedisChannel.Literal(dto.SiteId.HasValue
                ? RedisTelemetryChannels.StatsSite(dto.SiteId.Value)
                : RedisTelemetryChannels.StatsSiteNone),
            payload);
        if (batteryTypeId.HasValue)
            await sub.PublishAsync(RedisChannel.Literal(RedisTelemetryChannels.StatsType(batteryTypeId.Value)), payload);
        await sub.PublishAsync(RedisChannel.Literal(RedisTelemetryChannels.StatsAll), payload);
    }

    private static RedisValue Num(decimal? v) =>
        v is null ? "" : v.Value.ToString(CultureInfo.InvariantCulture);

    private static decimal? ParseNum(RedisValue v) =>
        v.IsNull || ((string?)v)?.Length == 0
            ? null
            : decimal.Parse((string)v!, CultureInfo.InvariantCulture);

    private static int ParseInt(RedisValue v) =>
        v.IsNull ? 0 : (int)v;
}
