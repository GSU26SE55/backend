using System.Diagnostics;
using System.Text.Json;
using BatteryService.Application.Common.Models;
using BatteryService.Application.CQRS.Command.SensorReading;
using BatteryService.Application.CQRS.Handler.SensorReading;
using BatteryService.Application.DTOs.Realtime;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Realtime;
using BatteryService.Domain.Entities;
using BatteryService.Infrastructure.Implements.Repositories;
using BatteryService.Infrastructure.Persistence;
using BatteryService.Infrastructure.Realtime;
using BatteryService.UnitTests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharedInfrastructure.Persistence.Interceptors;
using SharedInfrastructure.Services;

namespace BatteryService.IntegrationTests.Realtime;

/// <summary>
/// Sprint BE-IoT-Realtime <c>#623</c> — test SSE trên Redis THẬT (backplane pub/sub của
/// <c>BEIOT-RT-02</c>). Trước đây nhóm realtime chỉ có test thuần logic
/// (<c>BatteryRealtimeAuthorizationTests</c>, <c>RealtimeSummaryContractTests</c>) — không kịch bản
/// nào đi qua Redis, nên đường publisher → stream chưa từng được kiểm chứng.
///
/// <para><b>Phạm vi:</b> phủ publisher → Redis → stream, tức toàn bộ phần backend của SSE. Chặng
/// HTTP cuối (<c>SensorTelemetryStreamController</c> ghi <c>event:</c>/<c>data:</c>) KHÔNG nằm trong
/// đây — phần phân quyền của nó đã có <c>BatteryRealtimeAuthorizationTests</c> phủ riêng.</para>
///
/// <para><b>`Last-Event-ID` resume:</b> đã cài 2026-08-01 (#614) — replay bằng Redis Stream
/// <c>telemetry:replay:asset:{id}</c>, giữ <c>ReplayMaxEvents</c> bản ghi gần nhất, TTL
/// <c>ReplayTtlMinutes</c>. Test reconnect nằm ở cuối lớp này.</para>
/// </summary>
[Collection(nameof(RedisCollection))]
public class SseTelemetryStreamTests
{
    private readonly RedisFixture _redis;

    public SseTelemetryStreamTests(RedisFixture redis) => _redis = redis;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private (RedisTelemetryPublisher Publisher, RedisTelemetryStream Stream) Build(int summaryIntervalSeconds = 4)
    {
        var options = Options.Create(new RealtimeOptions
        {
            Enabled = true,
            HeartbeatSeconds = 30,
            SummaryIntervalSeconds = summaryIntervalSeconds
        });
        return (new RedisTelemetryPublisher(_redis.Redis, options, NullLogger<RedisTelemetryPublisher>.Instance),
                new RedisTelemetryStream(_redis.Redis, options));
    }

    private static LiveReadingDto Reading(Guid assetId, Guid customerId, decimal voltage = 51.2m,
        string? sourceCode = "primary") => new()
        {
            BatteryAssetId = assetId,
            CustomerId = customerId,
            Time = DateTime.UtcNow,
            Voltage = voltage,
            Current = 3.1m,
            Temperature = 29.5m,
            SocPercent = 90m,
            SensorSourceCode = sourceCode
        };

    // ---------------------------------------------------------------- 1) e2e < 1s

    [Fact]
    [Trait("Category", "Performance")]
    public async Task Publish_ReachesSubscriberAsReadingEvent_UnderOneSecond()
    {
        var assetId = Guid.NewGuid();
        var (publisher, stream) = Build();
        var scope = TelemetryScope.Parse($"asset:{assetId}")!.Value;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var received = new TaskCompletionSource<SseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

        var pump = Task.Run(async () =>
        {
            await foreach (var msg in stream.SubscribeAsync(scope, lastEventId: null, cts.Token))
            {
                // Bỏ qua `ping` heartbeat — chỉ quan tâm reading.
                if (msg.Event == "reading")
                { received.TrySetResult(msg); break; }
            }
        }, cts.Token);

        // Redis subscribe là bất đồng bộ — publish quá sớm thì message rơi vào khoảng trống.
        await Task.Delay(700, cts.Token);

        var sw = Stopwatch.StartNew();
        await publisher.PublishAsync(new[] { Reading(assetId, Guid.NewGuid()) }, cts.Token);

        var completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(10), cts.Token));
        completed.Should().Be(received.Task, "reading phải tới subscriber, không được rơi mất");
        sw.Stop();

        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1),
            "acceptance BEIOT-RT-10 yêu cầu reading tới subscriber dưới 1 giây");

        var dto = JsonSerializer.Deserialize<LiveReadingDto>(received.Task.Result.Data, Json)!;
        dto.BatteryAssetId.Should().Be(assetId);
        dto.Voltage.Should().Be(51.2m);

        cts.Cancel();
        await Task.WhenAny(pump, Task.Delay(2000));
    }

    // ---------------------------------------------------------------- 2) throttle summary

    [Fact]
    public async Task MultiAssetScope_EmitsSummary_AtMostOncePerInterval()
    {
        var a1 = Guid.NewGuid();
        var a2 = Guid.NewGuid();
        const int intervalSeconds = 2;
        var (publisher, stream) = Build(intervalSeconds);
        var scope = TelemetryScope.Parse($"assets:{a1},{a2}")!.Value;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var summaries = new List<DateTime>();

        var pump = Task.Run(async () =>
        {
            await foreach (var msg in stream.SubscribeAsync(scope, lastEventId: null, cts.Token))
            {
                if (msg.Event == "summary")
                    lock (summaries)
                        summaries.Add(DateTime.UtcNow);
            }
        }, cts.Token);

        await Task.Delay(700, cts.Token);

        // Bơm dồn dập 20 reading trong ~2 giây. Không throttle thì subscriber nhận ~20 message.
        var flood = TimeSpan.FromSeconds(2 * intervalSeconds);
        var stop = DateTime.UtcNow + flood;
        while (DateTime.UtcNow < stop)
        {
            await publisher.PublishAsync(new[] { Reading(a1, Guid.NewGuid()), Reading(a2, Guid.NewGuid()) }, cts.Token);
            await Task.Delay(100, cts.Token);
        }
        // Chờ thêm 1 nhịp để tick cuối kịp phát.
        await Task.Delay(TimeSpan.FromSeconds(intervalSeconds + 1), cts.Token);

        cts.Cancel();
        await Task.WhenAny(pump, Task.Delay(2000));

        int count;
        lock (summaries)
            count = summaries.Count;

        count.Should().BeGreaterThan(0, "scope nhiều pin phải phát event `summary`");
        // Cửa sổ quan sát ≈ flood + 1 nhịp chờ. Cho dư 1 message để không phụ thuộc thời điểm tick đầu.
        var maxExpected = (int)Math.Ceiling((flood.TotalSeconds + intervalSeconds + 1) / intervalSeconds) + 1;
        count.Should().BeLessThanOrEqualTo(maxExpected,
            $"coalescer phải gộp — tối đa ~1 summary mỗi {intervalSeconds}s, không phải mỗi reading");
    }

    // ---------------------------------------------------------------- 3) outlier không lên stream

    /// <summary>
    /// Chạy <b>ingest handler THẬT</b> với 1 reading hợp lệ + 1 reading outlier (1500V, vượt ngưỡng
    /// <c>MaxVoltage = 1000V</c> của <c>#IoT2-17</c>), bắt danh sách mà handler đưa cho
    /// <see cref="ITelemetryPublisher"/>.
    ///
    /// <para>Đây mới là hợp đồng đáng kiểm: tầng publish KHÔNG tự lọc outlier — nó publish nguyên
    /// những gì handler đưa. Nếu handler để lọt outlier thì số đo rác lên thẳng chart của Customer.
    /// (Test chỉ publish reading hợp lệ rồi khẳng định "không thấy 1500V" là lặp thừa — không bơm
    /// thì đương nhiên không thấy.)</para>
    /// </summary>
    [Fact]
    public async Task Ingest_RejectsOutlier_SoItNeverReachesTelemetryPublisher()
    {
        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"sse-outlier-{Guid.NewGuid()}")
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options,
            new AuditableEntityInterceptor(new CurrentUserService(new HttpContextAccessor())));

        var assetId = Guid.NewGuid();
        db.BatteryAssets.Add(new BatteryAsset
        {
            Id = assetId,
            SerialNumber = "BAT-OUTLIER",
            SiteId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid()
        });
        await db.SaveChangesAsync();

        var spy = new CapturingTelemetryPublisher();
        var handler = new BatchIngestSensorReadingsCommandHandler(
            new UnitOfWork(db),
            new NoopIotMetricsRecorder(),
            new NoopIotCalibrationCache(),
            spy,
            new NoopTelemetryStatsService(),
            NullLogger<BatchIngestSensorReadingsCommandHandler>.Instance);

        var now = DateTime.UtcNow;
        var result = await handler.Handle(new BatchIngestSensorReadingsCommand
        {
            Items = new List<SensorReadingItem>
            {
                new() { Time = now, BatteryAssetId = assetId, Voltage = 51.2m, Current = 3.1m,
                        Temperature = 29.5m, SocPercent = 90m },
                // 1500V > MaxVoltage 1000V ⇒ phải bị loại TRƯỚC khi tới publisher.
                new() { Time = now, BatteryAssetId = assetId, Voltage = 1500m, Current = 3.1m,
                        Temperature = 29.5m, SocPercent = 90m }
            }
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        spy.Published.Should().ContainSingle(
            "chỉ reading hợp lệ được publish — outlier bị loại ở ingest nên không có gì để lên stream");
        spy.Published[0].Voltage.Should().Be(51.2m);
        spy.Published.Should().NotContain(r => r.Voltage >= 1000m,
            "1500V lọt lên stream nghĩa là chart của Customer hiển thị số đo rác");

        // Chốt luôn ở tầng DB: outlier cũng không được ghi.
        var persisted = await db.SensorReadings.AsNoTracking().ToListAsync();
        persisted.Should().OnlyContain(r => r.Voltage < 1000m);
    }

    private sealed class CapturingTelemetryPublisher : ITelemetryPublisher
    {
        public List<LiveReadingDto> Published { get; } = new();

        public Task PublishAsync(IReadOnlyList<LiveReadingDto> readings, CancellationToken cancellationToken = default)
        {
            Published.AddRange(readings);
            return Task.CompletedTask;
        }
    }

    // ---------------------------------------------------------------- 4) Last-Event-ID resume

    /// <summary>
    /// Kịch bản thật: Customer đang xem chart 1 pin, rớt mạng, các reading trong lúc rớt vẫn được
    /// ingest. Khi nối lại, <c>EventSource</c> gửi <c>Last-Event-ID</c> và server phải <b>phát bù</b>
    /// đúng những reading bị bỏ lỡ — không thiếu, không trùng.
    ///
    /// Trước 2026-08-01 tính năng này không tồn tại: pub/sub không có lịch sử nên đoạn dữ liệu trong
    /// lúc rớt mạng mất vĩnh viễn, chart thủng một khúc mà không có lỗi nào báo.
    /// </summary>
    [Fact]
    public async Task Reconnect_WithLastEventId_ReplaysOnlyMissedReadings()
    {
        var assetId = Guid.NewGuid();
        var (publisher, stream) = Build();
        var scope = TelemetryScope.Parse($"asset:{assetId}")!.Value;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(40));

        // ─── Phiên 1: nhận reading #1 rồi "rớt mạng" (huỷ subscribe) ───
        string firstId;
        using (var s1 = CancellationTokenSource.CreateLinkedTokenSource(cts.Token))
        {
            var got = new TaskCompletionSource<SseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            var pump1 = Task.Run(async () =>
            {
                await foreach (var m in stream.SubscribeAsync(scope, lastEventId: null, s1.Token))
                    if (m.Event == "reading")
                    { got.TrySetResult(m); break; }
            }, s1.Token);

            await Task.Delay(700, cts.Token);
            await publisher.PublishAsync(new[] { Reading(assetId, Guid.NewGuid(), voltage: 1m) }, cts.Token);

            var done = await Task.WhenAny(got.Task, Task.Delay(TimeSpan.FromSeconds(10), cts.Token));
            done.Should().Be(got.Task, "phiên 1 phải nhận được reading đầu tiên");

            var first = got.Task.Result;
            first.Id.Should().NotBeNullOrEmpty("event `reading` ở scope 1 pin PHẢI có id thì client mới resume được");
            firstId = first.Id!;

            s1.Cancel();
            await Task.WhenAny(pump1, Task.Delay(2000));
        }

        // ─── Mất mạng: 3 reading vẫn được ingest, client không nhận được cái nào ───
        await publisher.PublishAsync(new[] { Reading(assetId, Guid.NewGuid(), voltage: 2m) }, cts.Token);
        await publisher.PublishAsync(new[] { Reading(assetId, Guid.NewGuid(), voltage: 3m) }, cts.Token);
        await publisher.PublishAsync(new[] { Reading(assetId, Guid.NewGuid(), voltage: 4m) }, cts.Token);

        // ─── Phiên 2: nối lại kèm Last-Event-ID ───
        var replayed = new List<LiveReadingDto>();
        var ids = new List<string?>();
        using var s2 = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        var pump2 = Task.Run(async () =>
        {
            await foreach (var m in stream.SubscribeAsync(scope, firstId, s2.Token))
            {
                if (m.Event != "reading")
                    continue;
                lock (replayed)
                {
                    ids.Add(m.Id);
                    replayed.Add(JsonSerializer.Deserialize<LiveReadingDto>(m.Data, Json)!);
                }
            }
        }, s2.Token);

        var ok = await WaitUntilAsync(() => { lock (replayed) return replayed.Count >= 3; }, TimeSpan.FromSeconds(15));
        // Chờ thêm để chắc chắn KHÔNG có bản thừa nào tới sau.
        await Task.Delay(1500, cts.Token);
        s2.Cancel();
        await Task.WhenAny(pump2, Task.Delay(2000));

        ok.Should().BeTrue("server phải phát bù đủ 3 reading bị bỏ lỡ trong lúc rớt mạng");

        lock (replayed)
        {
            replayed.Select(r => r.Voltage).Should().Equal(new[] { 2m, 3m, 4m },
                "phải bù ĐÚNG 3 reading bị lỡ, ĐÚNG thứ tự — không kèm reading #1 mà client đã nhận");
            ids.Should().OnlyContain(i => !string.IsNullOrEmpty(i));
            ids.Should().OnlyHaveUniqueItems("mỗi sự kiện chỉ được phát 1 lần — trùng là chart vẽ lặp điểm");
            ids.Should().NotContain(firstId, "id client đã nhận thì không được phát lại");
        }
    }

    /// <summary>
    /// Kết nối mới (không có <c>Last-Event-ID</c>) KHÔNG được đổ toàn bộ lịch sử — client chỉ muốn
    /// số liệu từ lúc mở, phần cũ đã có REST <c>/history</c> lo (BEIOT-RT-06).
    /// </summary>
    [Fact]
    public async Task FreshConnect_WithoutLastEventId_DoesNotReplayHistory()
    {
        var assetId = Guid.NewGuid();
        var (publisher, stream) = Build();
        var scope = TelemetryScope.Parse($"asset:{assetId}")!.Value;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // Có sẵn lịch sử trong replay stream TRƯỚC khi client kết nối.
        await publisher.PublishAsync(new[] { Reading(assetId, Guid.NewGuid(), voltage: 9m) }, cts.Token);
        await publisher.PublishAsync(new[] { Reading(assetId, Guid.NewGuid(), voltage: 8m) }, cts.Token);

        var seen = new List<decimal>();
        using var s = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        var pump = Task.Run(async () =>
        {
            await foreach (var m in stream.SubscribeAsync(scope, lastEventId: null, s.Token))
            {
                if (m.Event != "reading")
                    continue;
                var dto = JsonSerializer.Deserialize<LiveReadingDto>(m.Data, Json)!;
                lock (seen)
                    seen.Add(dto.Voltage);
            }
        }, s.Token);

        await Task.Delay(700, cts.Token);
        await publisher.PublishAsync(new[] { Reading(assetId, Guid.NewGuid(), voltage: 7m) }, cts.Token);

        var ok = await WaitUntilAsync(() => { lock (seen) return seen.Count >= 1; }, TimeSpan.FromSeconds(10));
        await Task.Delay(1000, cts.Token);
        s.Cancel();
        await Task.WhenAny(pump, Task.Delay(2000));

        ok.Should().BeTrue();
        lock (seen)
        {
            seen.Should().Equal(new[] { 7m },
                "kết nối mới chỉ nhận số liệu phát sinh TỪ LÚC nối, không đổ lại 9m/8m trong lịch sử");
        }
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;
            await Task.Delay(100);
        }
        return condition();
    }
}
