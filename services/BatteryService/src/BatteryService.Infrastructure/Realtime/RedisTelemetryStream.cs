using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using BatteryService.Application.Common.Models;
using BatteryService.Application.DTOs.Realtime;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Realtime;
using BatteryService.Infrastructure.Observability;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace BatteryService.Infrastructure.Realtime;

/// <summary>
/// Sprint BE-IoT-Realtime (#614/#618) — <see cref="ITelemetryStream"/> dùng Redis pub/sub.
/// - 1 pin đơn (<see cref="TelemetryScope.IsSingleAsset"/>): forward mỗi reading thành event <c>reading</c> (full) + <c>ping</c>.
/// - Còn lại (nhiều asset/site, customer, type, all, site:none): coalesce latest-per-asset → <c>summary</c> throttle (§34.10.5).
/// Subscribe NHIỀU Redis channel cho 1 scope (multi-asset/site). Coalesce per-connection → an toàn multi-instance.
/// </summary>
public class RedisTelemetryStream : ITelemetryStream
{
    private readonly IConnectionMultiplexer _redis;
    private readonly RealtimeOptions _options;

    public RedisTelemetryStream(IConnectionMultiplexer redis, IOptions<RealtimeOptions> options)
    {
        _redis = redis;
        _options = options.Value;
    }

    // primary = BMS reading đầy đủ thông số. null/empty (single-source) cũng coi như primary.
    private static bool IsPrimary(string? sensorSourceCode) =>
        string.IsNullOrEmpty(sensorSourceCode) ||
        string.Equals(sensorSourceCode, "primary", StringComparison.OrdinalIgnoreCase);

    public async IAsyncEnumerable<SseMessage> SubscribeAsync(
        TelemetryScope scope,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var scopeLabel = scope.Label;   // nhãn chuẩn khớp keyword (asset/customer/site/type/all/site:none)
        var isAsset = scope.IsSingleAsset;
        var output = Channel.CreateUnbounded<SseMessage>(new UnboundedChannelOptions { SingleReader = true });
        var latest = new ConcurrentDictionary<Guid, LiveReadingDto>();

        var sub = _redis.GetSubscriber();
        var channels = RedisTelemetryChannels.ChannelsFor(scope)
            .Select(RedisChannel.Literal)
            .ToList();

        void Handler(RedisChannel _, RedisValue val)
        {
            if (!val.HasValue) return;
            LiveReadingDto? dto;
            try { dto = JsonSerializer.Deserialize<LiveReadingDto>(val!, RedisTelemetryPublisher.JsonOptions); }
            catch { return; }
            if (dto is null) return;

            if (isAsset)
            {
                output.Writer.TryWrite(new SseMessage("reading", val.ToString()));
                RealtimeMetrics.EventsPushed.WithLabels(scopeLabel, "reading").Inc();
            }
            else
            {
                // Coalesce mỗi pin — ƯU TIÊN giữ source `primary` (BMS, đủ V/I/T/SOC/SOH/cycle/
                // charging/bmsError). Không để `redundant` (temp=0) / `external-temp` (V/I=0) ghi
                // đè primary → tránh summary hiện số liệu một phần (§34.10.5). Trong 1 window
                // (latest.Clear mỗi tick) nếu chỉ có non-primary thì mới lấy non-primary mới nhất.
                latest.AddOrUpdate(dto.BatteryAssetId, dto, (_, existing) =>
                    IsPrimary(dto.SensorSourceCode) ? dto
                    : IsPrimary(existing.SensorSourceCode) ? existing
                    : dto);
            }
        }

        foreach (var ch in channels)
            await sub.SubscribeAsync(ch, Handler);
        RealtimeMetrics.ActiveConnections.WithLabels(scopeLabel).Inc();

        var pump = Task.Run(() => PumpAsync(scopeLabel, isAsset, latest, output.Writer, cancellationToken), cancellationToken);

        try
        {
            await foreach (var msg in output.Reader.ReadAllAsync(cancellationToken))
                yield return msg;
        }
        finally
        {
            foreach (var ch in channels)
                await sub.UnsubscribeAsync(ch, Handler);
            RealtimeMetrics.ActiveConnections.WithLabels(scopeLabel).Dec();
            output.Writer.TryComplete();
            try { await pump; } catch { /* shutdown */ }
        }
    }

    private async Task PumpAsync(
        string scopeLabel, bool isAsset,
        ConcurrentDictionary<Guid, LiveReadingDto> latest,
        ChannelWriter<SseMessage> writer, CancellationToken ct)
    {
        var heartbeat = TimeSpan.FromSeconds(Math.Max(1, _options.HeartbeatSeconds));
        var tickInterval = isAsset
            ? heartbeat
            : TimeSpan.FromSeconds(Math.Max(1, _options.SummaryIntervalSeconds));
        var secondsSincePing = 0.0;

        try
        {
            using var timer = new PeriodicTimer(tickInterval);
            while (await timer.WaitForNextTickAsync(ct))
            {
                if (isAsset)
                {
                    writer.TryWrite(new SseMessage("ping", "{}"));
                    RealtimeMetrics.EventsPushed.WithLabels(scopeLabel, "ping").Inc();
                    continue;
                }

                if (!latest.IsEmpty)
                {
                    // Đầy đủ thông số: mỗi item là LiveReadingDto HOÀN CHỈNH (parity với event
                    // `reading`) — không rút gọn. Coalescer ở trên đã ưu tiên source primary.
                    var items = latest.Values.ToList();
                    latest.Clear();
                    var summary = new BatterySummaryDto { ScopeType = scopeLabel, Items = items };
                    writer.TryWrite(new SseMessage("summary", JsonSerializer.Serialize(summary, RedisTelemetryPublisher.JsonOptions)));
                    RealtimeMetrics.EventsPushed.WithLabels(scopeLabel, "summary").Inc();
                    secondsSincePing = 0;
                }
                else
                {
                    secondsSincePing += tickInterval.TotalSeconds;
                    if (secondsSincePing >= heartbeat.TotalSeconds)
                    {
                        writer.TryWrite(new SseMessage("ping", "{}"));
                        RealtimeMetrics.EventsPushed.WithLabels(scopeLabel, "ping").Inc();
                        secondsSincePing = 0;
                    }
                }
            }
        }
        catch (OperationCanceledException) { /* client disconnect / shutdown */ }
        finally { writer.TryComplete(); }
    }
}
