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

    /// <summary>Tách "&lt;streamId&gt; &lt;json&gt;" mà publisher gắn vào kênh asset.</summary>
    /// <remarks>
    /// Dung thứ payload KHÔNG có tiền tố (instance cũ publish trong lúc rolling deploy, hoặc replay
    /// bị tắt): khi đó coi cả chuỗi là JSON và id = null. Id của Redis Stream luôn dạng
    /// <c>&lt;số&gt;-&lt;số&gt;</c> nên không thể nhầm với JSON (bắt đầu bằng '{').
    /// </remarks>
    internal static (string? Id, string Json) SplitReplayEnvelope(string raw)
    {
        var sp = raw.IndexOf(' ');
        if (sp <= 0)
            return (null, raw);

        var head = raw.AsSpan(0, sp);
        var dash = head.IndexOf('-');
        if (dash <= 0)
            return (null, raw);

        foreach (var c in head)
            if (!char.IsAsciiDigit(c) && c != '-')
                return (null, raw);

        return (head.ToString(), raw[(sp + 1)..]);
    }

    public async IAsyncEnumerable<SseMessage> SubscribeAsync(
        TelemetryScope scope,
        string? lastEventId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var scopeLabel = scope.Label;   // nhãn chuẩn khớp keyword (asset/customer/site/type/all/site:none)
        var isAsset = scope.IsSingleAsset;
        var output = Channel.CreateUnbounded<SseMessage>(new UnboundedChannelOptions { SingleReader = true });
        var latest = new ConcurrentDictionary<Guid, LiveReadingDto>();
        // Id đã phát bù — dùng để loại bản live trùng (một sự kiện có thể vừa nằm trong XRANGE vừa
        // được pub/sub giao tới, vì ta subscribe TRƯỚC khi đọc lịch sử).
        var replayed = new HashSet<string>();

        var sub = _redis.GetSubscriber();
        var channels = RedisTelemetryChannels.ChannelsFor(scope)
            .Select(RedisChannel.Literal)
            .ToList();

        // Sprint Bonus NS-04 (#649) — kênh stats riêng, handler riêng (forward thẳng, KHÔNG parse
        // LiveReadingDto / coalesce). Đẩy trên MỌI scope (kể cả multi-asset): mỗi message stats là
        // cho 1 pin, FE phân biệt theo batteryAssetId + window.
        var statsChannels = RedisTelemetryChannels.StatsChannelsFor(scope)
            .Select(RedisChannel.Literal)
            .ToList();

        void Handler(RedisChannel _, RedisValue val)
        {
            if (!val.HasValue)
                return;
            var (eventId, json) = SplitReplayEnvelope(val.ToString());

            LiveReadingDto? dto;
            try
            { dto = JsonSerializer.Deserialize<LiveReadingDto>(json, RedisTelemetryPublisher.JsonOptions); }
            catch { return; }
            if (dto is null)
                return;

            if (isAsset)
            {
                // Chống phát trùng: sự kiện vừa được phát bù ở bước replay thì bỏ qua bản live.
                if (eventId is not null && replayed.Contains(eventId))
                    return;

                output.Writer.TryWrite(new SseMessage("reading", json, eventId));
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

        void StatsHandler(RedisChannel _, RedisValue val)
        {
            if (!val.HasValue)
                return;
            output.Writer.TryWrite(new SseMessage("stats", val.ToString()));
            RealtimeMetrics.EventsPushed.WithLabels(scopeLabel, "stats").Inc();
        }

        foreach (var ch in channels)
            await sub.SubscribeAsync(ch, Handler);
        foreach (var ch in statsChannels)
            await sub.SubscribeAsync(ch, StatsHandler);
        RealtimeMetrics.ActiveConnections.WithLabels(scopeLabel).Inc();

        // ─── Phát bù (#614) ───
        // THỨ TỰ QUAN TRỌNG: subscribe pub/sub xong mới đọc lịch sử. Nếu đọc trước rồi mới subscribe
        // thì sự kiện rơi vào khe giữa 2 bước sẽ mất hẳn. Làm ngược lại chỉ sinh trùng — và trùng thì
        // lọc được bằng `replayed`.
        var backlog = new List<SseMessage>();
        if (isAsset && !string.IsNullOrWhiteSpace(lastEventId))
        {
            foreach (var m in await ReadBacklogAsync(scope.Ids[0], lastEventId!))
            {
                if (m.Id is not null)
                    replayed.Add(m.Id);
                backlog.Add(m);
            }
        }

        var pump = Task.Run(() => PumpAsync(scopeLabel, isAsset, latest, output.Writer, cancellationToken), cancellationToken);

        try
        {
            foreach (var msg in backlog)
            {
                RealtimeMetrics.EventsPushed.WithLabels(scopeLabel, "reading").Inc();
                yield return msg;
            }

            await foreach (var msg in output.Reader.ReadAllAsync(cancellationToken))
                yield return msg;
        }
        finally
        {
            foreach (var ch in channels)
                await sub.UnsubscribeAsync(ch, Handler);
            foreach (var ch in statsChannels)
                await sub.UnsubscribeAsync(ch, StatsHandler);
            RealtimeMetrics.ActiveConnections.WithLabels(scopeLabel).Dec();
            output.Writer.TryComplete();
            try
            { await pump; }
            catch { /* shutdown */ }
        }
    }

    /// <summary>
    /// Đọc các reading SAU <paramref name="lastEventId"/> từ stream replay của pin.
    ///
    /// <para><c>StreamRangeAsync</c> của Redis là <b>bao gồm cả 2 đầu</b>, nên truyền thẳng
    /// <paramref name="lastEventId"/> sẽ phát lại chính sự kiện client ĐÃ nhận. Redis 6.2+ hỗ trợ
    /// tiền tố <c>(</c> để loại trừ, nhưng để không phụ thuộc phiên bản, ta đọc bao gồm rồi tự bỏ
    /// phần tử đầu nếu trùng id.</para>
    ///
    /// <para>Lỗi Redis ở đây KHÔNG được làm hỏng kết nối: mất phát bù còn hơn mất luôn luồng
    /// realtime — client vẫn nhận được số liệu mới.</para>
    /// </summary>
    private async Task<IReadOnlyList<SseMessage>> ReadBacklogAsync(Guid assetId, string lastEventId)
    {
        try
        {
            var db = _redis.GetDatabase();
            var entries = await db.StreamRangeAsync(
                RedisTelemetryChannels.AssetReplay(assetId),
                minId: lastEventId,
                maxId: "+",
                count: Math.Max(1, _options.ReplayMaxEvents));

            var result = new List<SseMessage>(entries.Length);
            foreach (var e in entries)
            {
                var id = e.Id.ToString();
                if (id == lastEventId)
                    continue;   // client đã nhận rồi

                var payload = e.Values
                    .FirstOrDefault(v => v.Name == RedisTelemetryPublisher.ReplayPayloadField).Value;
                if (payload.HasValue)
                    result.Add(new SseMessage("reading", payload.ToString(), id));
            }
            return result;
        }
        catch
        {
            return Array.Empty<SseMessage>();
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
