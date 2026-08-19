using System.Diagnostics;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Redis;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.Persistence;
using TicketService.IntegrationTests.Fixtures;
using Xunit.Abstractions;

namespace TicketService.IntegrationTests.Performance;

/// <summary>
/// <b>Sprint Chat — DoD: "SignalR broadcast p99 &lt; 500ms với 100 concurrent user".</b>
///
/// <para><b>Đo cái gì:</b> khoảng thời gian từ lúc <c>SignalRTicketChatNotifier</c> phát tin
/// (đường production thật, không phải gọi thẳng <c>IHubContext</c>) tới lúc <b>từng client</b> chạy
/// xong callback <c>ChatAdded</c>. 100 client thật, kết nối WebSocket thật, cùng ở trong group của
/// một ticket.</para>
///
/// <para><b>Vì sao phải có client thật:</b> nếu chỉ mock <c>IHubContext</c> rồi đếm số lần gọi thì
/// ta đo "hàm có được gọi không", chứ không đo độ trễ — mà DoD hỏi đúng độ trễ. Client thật bắt
/// được cả chi phí serialize, chi phí fan-out theo group, và chi phí backpressure khi 100 socket
/// cùng nhận một lúc.</para>
///
/// <para><b>Giới hạn phải nói rõ:</b> chạy trên <c>TestServer</c> in-process nên KHÔNG có chặng
/// mạng thật (không TCP loopback, không TLS, không Internet). Con số đo được là <b>độ trễ fan-out
/// phía server</b> — phần hệ thống này kiểm soát được. Độ trễ người dùng cuối = số này + RTT mạng.
/// Với ngưỡng 500ms và biên đo được, RTT thực tế (vài chục ms) không làm đổi kết luận; nhưng đây
/// là phép đo server-side, không phải phép đo end-to-end qua Internet.</para>
///
/// <para><b>Vì sao 10 vòng chứ không 1:</b> một vòng cho đúng 100 mẫu và mẫu đầu luôn dính chi phí
/// khởi động (JIT, cấp buffer). 10 vòng cho 1000 mẫu — p99 mới có nghĩa thống kê.</para>
///
/// <para><b>Redis backplane THẬT, nhưng là container cục bộ:</b> <c>Program.cs</c> gắn
/// <c>AddStackExchangeRedis</c> khi có <c>ConnectionStrings:Redis</c>, nên trong production mọi
/// broadcast đều đi vòng qua Redis. Bỏ backplane đi thì số đo đẹp hơn thực tế, nên test giữ nguyên
/// backplane. Nhưng <c>EnvFileLoader</c> nạp <c>.env</c> ở gốc repo — trong đó
/// <c>ConnectionStrings__Redis</c> trỏ tới <b>Upstash trên cloud</b>. Bắn 1000 lượt broadcast vào
/// một Redis dùng chung là việc không được làm, nên test ghi đè bằng Redis container cục bộ.</para>
/// </summary>
[Trait("Category", "Performance")]
public class SignalRBroadcastSloTests : IAsyncLifetime
{
    private const int ConcurrentUsers = 100;
    private const int Rounds = 10;
    private const int P99ThresholdMs = 500;

    private static readonly Guid TicketId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly ITestOutputHelper _out;
    private readonly List<HubConnection> _connections = new();

    private const string RedisEnvKey = "ConnectionStrings__Redis";

    private RedisContainer _redis = null!;
    private TicketApiFactory _factory = null!;
    private string? _previousRedisEnv;

    public SignalRBroadcastSloTests(ITestOutputHelper output) => _out = output;

    public async Task InitializeAsync()
    {
        _redis = new RedisBuilder("redis:7-alpine")
            .WithCleanUp(true)
            .Build();
        await _redis.StartAsync();

        // ── Vì sao phải ghi đè bằng BIẾN MÔI TRƯỜNG, không phải ConfigureAppConfiguration ──
        // Program.cs đọc `builder.Configuration.GetConnectionString("Redis")` và gọi
        // AddStackExchangeRedis NGAY tại đó — tức TRƯỚC `builder.Build()`. Mà các delegate
        // ConfigureWebHost của WebApplicationFactory chỉ được áp vào lúc Build() bị chặn, nên
        // mọi giá trị thêm qua ConfigureAppConfiguration đến SAU khi backplane đã đăng ký xong.
        // (Đây đúng là lý do TicketApiFactory phải set ConnectionStrings__TicketDb trong static ctor.)
        // EnvFileLoader KHÔNG ghi đè env var đã có sẵn, nên set trước là chắc chắn thắng .env.
        _previousRedisEnv = Environment.GetEnvironmentVariable(RedisEnvKey);
        var localRedis = $"{_redis.GetConnectionString()},abortConnect=false";
        Environment.SetEnvironmentVariable(RedisEnvKey, localRedis);

        _factory = new TicketApiFactory();

        // Chốt an toàn: nếu ghi đè hụt, app sẽ rơi về ConnectionStrings__Redis trong .env — tức
        // Upstash trên cloud. Dừng ngay thay vì bắn 1000 lượt broadcast vào một Redis dùng chung.
        var resolvedRedis = _factory.Services
            .GetRequiredService<IConfiguration>().GetConnectionString("Redis");
        _out.WriteLine($"Redis backplane đang dùng: {resolvedRedis}");
        resolvedRedis.Should().Be(localRedis,
            "test này PHẢI chạy trên Redis container cục bộ, tuyệt đối không chạm Redis cloud trong .env");

        // Chạm vào Services để WebApplicationFactory dựng host trước khi dùng Server.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketDbContext>();
            db.Tickets.Add(new Ticket
            {
                Id = TicketId,
                Code = "TKT-SIGNALR-SLO",
                CustomerId = Guid.Parse(TestAuthHandler.UserId),
                Title = "SignalR broadcast SLO",
                Description = "seed",
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < ConcurrentUsers; i++)
        {
            var conn = BuildConnection();
            await conn.StartAsync();
            // Join tuần tự: TicketApiFactory dùng MỘT SqliteConnection in-memory dùng chung, bắn 100
            // JoinTicket song song sẽ đụng nhau ở tầng SQLite. Đây là bước dựng cảnh, không tính giờ.
            await conn.InvokeAsync("JoinTicket", TicketId.ToString());
            _connections.Add(conn);
        }
        sw.Stop();

        _out.WriteLine($"Dựng {ConcurrentUsers} kết nối WebSocket + JoinTicket: {sw.Elapsed.TotalSeconds:F1}s");
    }

    public async Task DisposeAsync()
    {
        foreach (var conn in _connections)
            await conn.DisposeAsync();
        if (_factory is not null)
            await _factory.DisposeAsync();

        // Trả env var về nguyên trạng — nó là biến toàn tiến trình, để nguyên sẽ đổi hành vi
        // của các test khác chạy sau trong cùng assembly.
        Environment.SetEnvironmentVariable(RedisEnvKey, _previousRedisEnv);

        if (_redis is not null)
            await _redis.DisposeAsync();
    }

    private HubConnection BuildConnection() =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "hubs/ticket-chats"), o =>
            {
                o.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                o.Transports = HttpTransportType.WebSockets;
                o.SkipNegotiation = true;
                o.WebSocketFactory = async (context, ct) =>
                    await _factory.Server.CreateWebSocketClient().ConnectAsync(context.Uri, ct);
            })
            // Phải khớp JSON protocol mà Program.cs cấu hình cho server: camelCase + enum dạng
            // chuỗi. Thiếu JsonStringEnumConverter thì client ném ngay khi gặp "authorRole":"Staff",
            // callback không bao giờ chạy, và triệu chứng nhìn y hệt "tin bị rơi".
            .AddJsonProtocol(o =>
            {
                o.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
                o.PayloadSerializerOptions.Converters.Add(
                    new System.Text.Json.Serialization.JsonStringEnumConverter());
            })
            .Build();

    [Fact]
    public async Task Broadcast_To100ConcurrentUsers_MeetsP99Under500ms()
    {
        _connections.Should().HaveCount(ConcurrentUsers);
        _connections.Should().OnlyContain(c => c.State == HubConnectionState.Connected,
            "phép đo chỉ có nghĩa khi đủ 100 client đang thực sự kết nối");

        var latencies = new List<double>(ConcurrentUsers * Rounds);

        for (var round = 0; round < Rounds; round++)
        {
            var received = new System.Collections.Concurrent.ConcurrentBag<double>();
            var all = new TaskCompletionSource();
            var count = 0;
            var subs = new List<IDisposable>(ConcurrentUsers);
            var clock = new Stopwatch();

            foreach (var conn in _connections)
            {
                subs.Add(conn.On<TicketChatDTO>("ChatAdded", _ =>
                {
                    received.Add(clock.Elapsed.TotalMilliseconds);
                    if (Interlocked.Increment(ref count) == ConcurrentUsers)
                        all.TrySetResult();
                }));
            }

            var chat = new TicketChatDTO
            {
                Id = Guid.NewGuid().ToString(),
                TicketId = TicketId.ToString(),
                AuthorUserId = TestAuthHandler.UserId,
                AuthorRole = ActorRoleEnum.Staff,
                AuthorDisplayName = "Staff",
                IsInternal = false,
                Body = $"broadcast vòng {round}",
                CreatedAt = DateTime.UtcNow,
            };

            using (var scope = _factory.Services.CreateScope())
            {
                var notifier = scope.ServiceProvider.GetRequiredService<ITicketChatRealtimeNotifier>();

                clock.Start();
                await notifier.NotifyChatAddedAsync(chat);

                var done = await Task.WhenAny(all.Task, Task.Delay(TimeSpan.FromSeconds(30)));
                clock.Stop();

                done.Should().BeSameAs(all.Task,
                    $"vòng {round}: chỉ {count}/{ConcurrentUsers} client nhận được tin trong 30s — " +
                    "tin rơi thì con số độ trễ vô nghĩa");
            }

            foreach (var s in subs)
                s.Dispose();
            latencies.AddRange(received);
        }

        latencies.Should().HaveCount(ConcurrentUsers * Rounds,
            "mỗi vòng phải đủ 100 mẫu, không được thiếu client nào");

        latencies.Sort();
        double At(double q) => latencies[Math.Min(latencies.Count - 1, (int)Math.Ceiling(q * latencies.Count) - 1)];
        var p50 = At(0.50);
        var p95 = At(0.95);
        var p99 = At(0.99);

        _out.WriteLine($"users={ConcurrentUsers} rounds={Rounds} mẫu={latencies.Count}  " +
                       $"p50={p50:F1}ms p95={p95:F1}ms p99={p99:F1}ms max={latencies[^1]:F1}ms");

        p99.Should().BeLessThan(P99ThresholdMs,
            $"DoD Sprint Chat yêu cầu SignalR broadcast p99 < {P99ThresholdMs}ms với {ConcurrentUsers} " +
            $"concurrent user. Đo được p50={p50:F1}ms p95={p95:F1}ms p99={p99:F1}ms");
    }

    /// <summary>
    /// Không chỉ nhanh — phải phát ĐÚNG group. Tin nội bộ chỉ được vào
    /// <c>ticket:{id}:internal</c>; client nào không ở group đó không được nhận.
    ///
    /// <para>Ở đây mọi client đều mang role Admin (TestAuthHandler mặc định) nên đều ở cả 2 group —
    /// tức test này chốt rằng tin internal <b>vẫn tới</b> đúng số client có quyền, và số lượng nhận
    /// khớp chính xác chứ không nhân đôi vì client ở 2 group cùng lúc.</para>
    /// </summary>
    [Fact]
    public async Task InternalBroadcast_ReachesInternalGroupExactlyOncePerClient()
    {
        // Khoá theo CHỈ SỐ chứ không theo ConnectionId: với SkipNegotiation = true, client không
        // qua bước negotiate nên `HubConnection.ConnectionId` là null. Dùng nó làm khoá dictionary
        // sẽ ném ngay trong callback, mà SignalR nuốt lỗi callback — triệu chứng nhìn y hệt
        // "tin không tới", cực dễ chẩn đoán nhầm.
        var perConnection = new System.Collections.Concurrent.ConcurrentDictionary<int, int>();
        var all = new TaskCompletionSource();
        var count = 0;
        var subs = new List<IDisposable>(ConcurrentUsers);

        for (var i = 0; i < _connections.Count; i++)
        {
            var index = i;
            subs.Add(_connections[i].On<TicketChatDTO>("ChatAdded", _ =>
            {
                perConnection.AddOrUpdate(index, 1, (_, v) => v + 1);
                if (Interlocked.Increment(ref count) == ConcurrentUsers)
                    all.TrySetResult();
            }));
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var notifier = scope.ServiceProvider.GetRequiredService<ITicketChatRealtimeNotifier>();
            await notifier.NotifyChatAddedAsync(new TicketChatDTO
            {
                Id = Guid.NewGuid().ToString(),
                TicketId = TicketId.ToString(),
                AuthorUserId = TestAuthHandler.UserId,
                AuthorRole = ActorRoleEnum.Staff,
                AuthorDisplayName = "Staff",
                IsInternal = true,
                Body = "ghi chú nội bộ",
                CreatedAt = DateTime.UtcNow,
            });

            var done = await Task.WhenAny(all.Task, Task.Delay(TimeSpan.FromSeconds(30)));
            done.Should().BeSameAs(all.Task, $"chỉ {count}/{ConcurrentUsers} client nhận được tin nội bộ");
        }

        // Chờ ngắn TRƯỚC khi gỡ subscription — để nếu có bản lặp (client ở cả 2 group mà notifier
        // phát nhầm cả 2 nơi) thì nó vẫn bị đếm. Gỡ sub trước rồi mới chờ là tự bịt mắt mình.
        await Task.Delay(500);
        foreach (var s in subs)
            s.Dispose();

        perConnection.Should().HaveCount(ConcurrentUsers);
        perConnection.Values.Should().OnlyContain(v => v == 1,
            "mỗi client chỉ được nhận ĐÚNG MỘT bản — client vừa ở group public vừa ở internal mà " +
            "notifier phát cả 2 group thì UI sẽ hiện tin trùng");
    }
}
