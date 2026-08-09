using System.Diagnostics;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using SharedInfrastructure.Persistence.Interceptors;
using SharedInfrastructure.Services;
using Testcontainers.PostgreSql;
using TicketService.Application.CQRS.Handler.Chats;
using TicketService.Application.CQRS.Query.Ticket;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.Implements.Repositories;
using TicketService.Infrastructure.Persistence;
using Xunit.Abstractions;

namespace TicketService.IntegrationTests.Performance;

/// <summary>
/// <b>Sprint Chat — DoD: "Performance SLO đạt: GET chat list p95 &lt; 200ms với 1000 chat/ticket."</b>
///
/// <para><b>Vì sao phải là Postgres thật:</b> bộ integration test còn lại chạy SQLite in-memory cho
/// nhanh. SQLite in-memory không có index giống Postgres, không có planner giống Postgres, và không
/// chạm đĩa — đo trên đó chỉ ra một con số đẹp vô nghĩa. SLO nói về hệ thống thật nên phải đo trên
/// đúng engine production.</para>
///
/// <para><b>Vì sao tắt cache:</b> <c>TicketChatsQueryHandler</c> có đường tắt Redis cho truy vấn mặc
/// định (trang 1, pageSize 10, không filter). Nếu để cache bật thì phép đo chỉ nói "Redis nhanh" —
/// điều ai cũng biết. Ta đo <b>đường chậm nhất</b>: cache trượt, phải xuống DB. Đạt SLO ở đường này
/// thì đường có cache hiển nhiên đạt.</para>
///
/// <para><b>Vì sao seed nhiều ticket chứ không chỉ một:</b> nếu bảng chỉ có đúng 1000 dòng của 1
/// ticket thì filter <c>ticket_id</c> khớp toàn bộ bảng — Postgres quét tuần tự vẫn nhanh và ta
/// không biết index có ăn hay không. Seed thêm 19 ticket nhiễu để filter thật sự phải chọn lọc.</para>
///
/// <para>Gắn <c>Category=Performance</c> — chạy bằng <c>make test-perf</c>, loại khỏi CI thường vì
/// số đo phụ thuộc tải máy.</para>
/// </summary>
[Trait("Category", "Performance")]
public class ChatSloTests : IAsyncLifetime
{
    private const int ChatsPerTicket = 1000;
    private const int NoiseTickets = 19;
    private const int P95ThresholdMs = 200;
    private const int Iterations = 60;

    private static readonly Guid TargetTicketId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid StaffId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid CustomerId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly ITestOutputHelper _out;
    public ChatSloTests(ITestOutputHelper output) => _out = output;

    [Obsolete]
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("ticket_slo")
        .WithUsername("test")
        .WithPassword("test")
        .WithCleanUp(true)
        .Build();

    [Obsolete]
    private TicketDbContext NewContext()
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(x => x.UserId).Returns((string?)null);

        var options = new DbContextOptionsBuilder<TicketDbContext>()
            .UseNpgsql(_pg.GetConnectionString())
            .Options;

        return new TicketDbContext(options, new AuditableEntityInterceptor(currentUser.Object));
    }

    [Obsolete]
    public async Task InitializeAsync()
    {
        await _pg.StartAsync();

        await using (var db = NewContext())
            await db.Database.MigrateAsync();

        await SeedAsync();
    }

    [Obsolete]
    public async Task DisposeAsync() => await _pg.DisposeAsync();

    [Obsolete]
    private async Task SeedAsync()
    {
        var sw = Stopwatch.StartNew();
        await using var db = NewContext();

        var tickets = new List<Ticket>();
        var chats = new List<TicketChat>();

        for (var t = 0; t <= NoiseTickets; t++)
        {
            var isTarget = t == 0;
            var ticketId = isTarget ? TargetTicketId : Guid.NewGuid();

            var ticket = new Ticket
            {
                Id = ticketId,
                Code = $"TKT-SLO-{t:D4}",
                CustomerId = CustomerId,
                Title = $"SLO ticket {t}",
                Description = "seed",
                CreatedAt = DateTime.UtcNow.AddDays(-30),
                Assignments = new List<TicketAssignment>
                {
                    new()
                    {
                        Id = Guid.NewGuid(),
                        TicketId = ticketId,
                        StaffId = StaffId,
                        Role = AssignmentRoleEnum.PrimaryHandler
                    }
                }
            };
            tickets.Add(ticket);

            for (var i = 0; i < ChatsPerTicket; i++)
            {
                chats.Add(new TicketChat
                {
                    Id = Guid.NewGuid(),
                    TicketId = ticketId,
                    Ticket = ticket,
                    AuthorUserId = i % 2 == 0 ? StaffId : CustomerId,
                    AuthorRole = i % 2 == 0 ? ActorRoleEnum.Staff : ActorRoleEnum.Customer,
                    AuthorDisplayName = i % 2 == 0 ? "Staff" : "Customer",
                    // 1/5 tin nội bộ — để nhánh lọc IsInternal của Customer thực sự phải làm việc.
                    IsInternal = i % 5 == 0,
                    // Vài tin ghim — ORDER BY IsPinned DESC, CreatedAt DESC phải sắp thật.
                    IsPinned = i % 250 == 0,
                    Body = $"Tin nhắn kiểm thử số {i} cho ticket {t} — nội dung đủ dài để không bị nén tầm thường.",
                    BodyFormat = ChatBodyFormatEnum.PlainText,
                    CreatedAt = DateTime.UtcNow.AddMinutes(-(ChatsPerTicket - i)),
                });
            }
        }

        db.Tickets.AddRange(tickets);
        db.TicketChats.AddRange(chats);
        await db.SaveChangesAsync();

        await db.Database.ExecuteSqlRawAsync("ANALYZE;");

        sw.Stop();
        _out.WriteLine($"Seed {tickets.Count} ticket x {ChatsPerTicket} chat = {chats.Count:N0} dòng " +
                       $"+ ANALYZE: {sw.Elapsed.TotalSeconds:F1}s");
    }

    /// <summary>
    /// Đường mặc định FE gọi khi mở tab chat: trang 1, 10 tin mới nhất. Đo với cache TRƯỢT.
    /// </summary>
    [Fact]
    [Obsolete]
    public async Task ChatList_DefaultPage_With1000ChatsPerTicket_MeetsP95Under200ms()
    {
        var (p50, p95, p99, max) = await MeasureAsync(i => new TicketChatsQuery
        {
            TicketId = TargetTicketId,
            ActorUserId = StaffId,
            ActorRoles = new[] { "Staff" },
            PageNumber = 1,
            PageSize = 10,
        });

        _out.WriteLine($"[trang 1, pageSize 10, cache MISS] n={Iterations}  " +
                       $"p50={p50:F1}ms p95={p95:F1}ms p99={p99:F1}ms max={max:F1}ms");

        p95.Should().BeLessThan(P95ThresholdMs,
            $"DoD Sprint Chat yêu cầu GET chat list p95 < {P95ThresholdMs}ms với {ChatsPerTicket} chat/ticket. " +
            $"Đo được p50={p50:F1}ms p95={p95:F1}ms p99={p99:F1}ms");
    }

    /// <summary>
    /// Trang sâu — <c>OFFSET 990</c>. Đây là chỗ phân trang offset hay gãy: Postgres vẫn phải sắp
    /// toàn bộ rồi bỏ đi 990 dòng. Nếu SLO chỉ đạt ở trang 1 thì người dùng cuộn lên đầu hội thoại
    /// sẽ thấy chậm — DoD nói "GET chat list", không nói "chỉ trang 1".
    /// </summary>
    [Fact]
    [Obsolete]
    public async Task ChatList_DeepPage_MeetsP95Under200ms()
    {
        var (p50, p95, p99, max) = await MeasureAsync(i => new TicketChatsQuery
        {
            TicketId = TargetTicketId,
            ActorUserId = StaffId,
            ActorRoles = new[] { "Staff" },
            PageNumber = ChatsPerTicket / 10, // trang cuối
            PageSize = 10,
        });

        _out.WriteLine($"[trang cuối (offset {ChatsPerTicket - 10}), cache MISS] n={Iterations}  " +
                       $"p50={p50:F1}ms p95={p95:F1}ms p99={p99:F1}ms max={max:F1}ms");

        p95.Should().BeLessThan(P95ThresholdMs,
            $"trang sâu cũng phải đạt SLO. Đo được p50={p50:F1}ms p95={p95:F1}ms p99={p99:F1}ms");
    }

    /// <summary>
    /// Có filter tìm kiếm — đường này KHÔNG BAO GIỜ vào cache (điều kiện <c>isDefaultQuery</c> loại
    /// mọi truy vấn có <c>Search</c>). Tức trong production đây luôn là đường xuống DB.
    /// </summary>
    [Fact]
    [Obsolete]
    public async Task ChatList_WithSearchFilter_MeetsP95Under200ms()
    {
        var (p50, p95, p99, max) = await MeasureAsync(i => new TicketChatsQuery
        {
            TicketId = TargetTicketId,
            ActorUserId = StaffId,
            ActorRoles = new[] { "Staff" },
            PageNumber = 1,
            PageSize = 20,
            Search = $"số {i % 100}",
        });

        _out.WriteLine($"[search LIKE, không bao giờ cache] n={Iterations}  " +
                       $"p50={p50:F1}ms p95={p95:F1}ms p99={p99:F1}ms max={max:F1}ms");

        p95.Should().BeLessThan(P95ThresholdMs,
            $"tìm kiếm trong chat là đường luôn xuống DB. Đo được p50={p50:F1}ms p95={p95:F1}ms p99={p99:F1}ms");
    }

    /// <summary>
    /// Góc nhìn Customer — thêm nhánh <c>WHERE NOT is_internal</c> và không được thấy tin nội bộ.
    /// Kiểm luôn tính đúng đắn ngay trong bài đo: nhanh mà lộ tin nội bộ thì vô nghĩa.
    /// </summary>
    [Fact]
    [Obsolete]
    public async Task ChatList_AsCustomer_FiltersInternal_AndMeetsP95Under200ms()
    {
        var handler = NewHandler(out var db);
        await using (db)
        {
            var probe = await handler.Handle(new TicketChatsQuery
            {
                TicketId = TargetTicketId,
                ActorUserId = CustomerId,
                ActorRoles = new[] { "Customer" },
                PageNumber = 1,
                PageSize = 50,
            }, CancellationToken.None);

            probe.IsSuccess.Should().BeTrue();
            probe.Data!.Items.Should().NotBeEmpty();
            probe.Data.Items.Should().OnlyContain(x => !x.IsInternal,
                "Customer không được thấy tin nội bộ — kiểm ngay trong bài đo để tốc độ không đánh đổi bằng rò rỉ");
            probe.Data.TotalItems.Should().Be(ChatsPerTicket - ChatsPerTicket / 5,
                "tổng của Customer phải trừ đúng phần tin nội bộ (1/5 tổng)");
        }

        var (p50, p95, p99, max) = await MeasureAsync(i => new TicketChatsQuery
        {
            TicketId = TargetTicketId,
            ActorUserId = CustomerId,
            ActorRoles = new[] { "Customer" },
            PageNumber = 1,
            PageSize = 10,
        });

        _out.WriteLine($"[góc nhìn Customer, lọc internal] n={Iterations}  " +
                       $"p50={p50:F1}ms p95={p95:F1}ms p99={p99:F1}ms max={max:F1}ms");

        p95.Should().BeLessThan(P95ThresholdMs,
            $"Đo được p50={p50:F1}ms p95={p95:F1}ms p99={p99:F1}ms");
    }

    // ───────────────────────────────────────────────────────────── hạ tầng đo

    [Obsolete]
    private TicketChatsQueryHandler NewHandler(out TicketDbContext db)
    {
        db = NewContext();
        return new TicketChatsQueryHandler(new UnitOfWork(db), new NoCacheChatCacheService());
    }

    [Obsolete]
    private async Task<(double p50, double p95, double p99, double max)> MeasureAsync(
        Func<int, TicketChatsQuery> queryFactory)
    {
        var handler = NewHandler(out var db);
        await using var _ = db;

        // Vòng khởi động — bỏ chi phí nạp plan/cache trang, không tính vào p95.
        for (var w = 0; w < 5; w++)
        {
            var warm = await handler.Handle(queryFactory(w), CancellationToken.None);
            warm.IsSuccess.Should().BeTrue("truy vấn phải thành công thì số đo mới có nghĩa");
        }

        var latencies = new List<double>(Iterations);
        for (var i = 0; i < Iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            var res = await handler.Handle(queryFactory(i), CancellationToken.None);
            sw.Stop();

            res.IsSuccess.Should().BeTrue();
            latencies.Add(sw.Elapsed.TotalMilliseconds);
        }

        latencies.Sort();
        double At(double q) => latencies[Math.Min(latencies.Count - 1, (int)Math.Ceiling(q * latencies.Count) - 1)];
        return (At(0.50), At(0.95), At(0.99), latencies[^1]);
    }

    /// <summary>
    /// Cache luôn trượt — buộc handler xuống DB mỗi lần. <c>SetPageAsync</c> nuốt lặng để không đo
    /// nhầm chi phí ghi cache vào độ trễ truy vấn.
    /// </summary>
    private sealed class NoCacheChatCacheService : IChatCacheService
    {
        public Task<CachedChatPage?> GetPageAsync(Guid ticketId, int pageNumber, int pageSize,
            bool canViewInternal, CancellationToken ct = default)
            => Task.FromResult<CachedChatPage?>(null);

        public Task SetPageAsync(Guid ticketId, int pageNumber, int pageSize, bool canViewInternal,
            List<TicketChatDTO> chats, int totalItems, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task InvalidateAsync(Guid ticketId, CancellationToken ct = default) => Task.CompletedTask;
    }
}
