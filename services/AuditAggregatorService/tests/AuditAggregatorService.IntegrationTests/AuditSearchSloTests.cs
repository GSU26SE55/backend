using System.Diagnostics;
using AuditAggregatorService.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;
using Xunit.Abstractions;

namespace AuditAggregatorService.IntegrationTests;

/// <summary>
/// <b>Sprint audit — DoD: "AuditAggregatorService SLO đạt: … search API p95 &lt; 200ms với 1M row."</b>
///
/// <para>Seed <b>1 triệu bản ghi thật</b> vào Postgres rồi đo p95 của đúng truy vấn mà
/// <c>AuditSearchQueryHandler</c> chạy (filter + <c>COUNT</c> + <c>ORDER BY occurred_at DESC</c> +
/// phân trang). Con số này chỉ có ý nghĩa khi bảng đủ lớn — đo trên vài trăm dòng thì index nào
/// cũng nhanh, và ta sẽ không bao giờ biết truy vấn có dùng index hay đang quét toàn bảng.</para>
///
/// <para><b>Seed bằng <c>generate_series</c> chứ không qua EF:</b> 1 triệu <c>INSERT</c> qua EF mất
/// hàng chục phút và đo cái sai (tốc độ EF, không phải tốc độ truy vấn). Một câu
/// <c>INSERT … SELECT FROM generate_series</c> chạy trong vài giây.</para>
///
/// <para><b>Gắn <c>Category=Performance</c>:</b> đây là phép đo tài nguyên máy — chạy song song cả
/// solution sẽ ra số vô nghĩa. Chạy bằng <c>make test-perf</c>.</para>
/// </summary>
[Trait("Category", "Performance")]
public class AuditSearchSloTests : IAsyncLifetime
{
    private const int RowCount = 1_000_000;
    private const int P95ThresholdMs = 200;
    private const int Iterations = 60;

    private readonly ITestOutputHelper _out;
    public AuditSearchSloTests(ITestOutputHelper output) => _out = output;

    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("audit_aggregate_slo")
        .WithUsername("test")
        .WithPassword("test")
        .WithCleanUp(true)
        .Build();

    private AuditAggregateDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AuditAggregateDbContext>()
            .UseNpgsql(_pg.GetConnectionString())
            .Options);

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        await using (var db = NewContext())
            await db.Database.MigrateAsync();

        await SeedAsync();
    }

    public async Task DisposeAsync() => await _pg.DisposeAsync();

    /// <summary>
    /// Sinh 1M dòng trải đều 180 ngày, xoay vòng qua 6 service / 4 severity để filter thực sự phải
    /// chọn lọc chứ không phải "khớp hết" hay "khớp không dòng nào".
    /// </summary>
    private async Task SeedAsync()
    {
        var sw = Stopwatch.StartNew();
        await using var conn = new NpgsqlConnection(_pg.GetConnectionString());
        await conn.OpenAsync();

        await using (var cmd = new NpgsqlCommand($"""
            INSERT INTO audit_aggregate (
                id, event_id, service_name, action_code, action_category, severity,
                target_type, target_id, target_display,
                actor_account_id, actor_role, actor_display, actor_ip, actor_user_agent,
                is_success, error_code, reason, metadata_json,
                correlation_id, causation_id, occurred_at, recorded_at,
                created_at, is_deleted)
            SELECT
                gen_random_uuid(), gen_random_uuid(),
                (ARRAY['AuthService','TicketService','BatteryService','FileStorageService','NotificationService','SmsService'])[1 + (i % 6)],
                (ARRAY['LoginSuccess','TicketCreated','BatteryUpdated','FileUploaded','PushSent','SmsQueued'])[1 + (i % 6)],
                (ARRAY['Authentication','DataModification','Security','DataAccess'])[1 + (i % 4)],
                (ARRAY['Info','Warning','Critical','Security'])[1 + (i % 4)],
                'Account', gen_random_uuid(), 'target-' || i,
                gen_random_uuid(), 'Admin', 'actor-' || i, '127.0.0.1', 'ua',
                true, NULL, NULL, NULL,
                gen_random_uuid(), NULL,
                NOW() - ((i % 180) || ' days')::interval - ((i % 86400) || ' seconds')::interval,
                NOW(), NOW(), false
            FROM generate_series(1, {RowCount}) AS s(i);
            """, conn) { CommandTimeout = 600 })
        {
            await cmd.ExecuteNonQueryAsync();
        }

        // ANALYZE để planner có thống kê — thiếu bước này Postgres có thể chọn seq-scan và số đo
        // phản ánh "planner chưa biết gì" chứ không phải hiệu năng thật của schema.
        await using (var analyze = new NpgsqlCommand("ANALYZE audit_aggregate;", conn) { CommandTimeout = 600 })
            await analyze.ExecuteNonQueryAsync();

        sw.Stop();
        _out.WriteLine($"Seed {RowCount:N0} dòng + ANALYZE: {sw.Elapsed.TotalSeconds:F1}s");
    }

    [Fact]
    public async Task SearchApi_With1MillionRows_MeetsP95Under200ms()
    {
        await using var db = NewContext();

        (await db.AuditAggregates.CountAsync()).Should().Be(RowCount,
            "phép đo chỉ có nghĩa khi bảng thật sự có 1M dòng");

        var latencies = new List<double>(Iterations);
        var services = new[] { "AuthService", "TicketService", "BatteryService" };
        var severities = new[] { "Info", "Warning", "Critical", "Security" };

        // Vòng khởi động — bỏ qua, để không tính chi phí nạp plan/cache vào p95.
        for (var w = 0; w < 5; w++)
            await RunSearchAsync(db, services[w % services.Length], severities[w % severities.Length]);

        for (var i = 0; i < Iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            await RunSearchAsync(db, services[i % services.Length], severities[i % severities.Length]);
            sw.Stop();
            latencies.Add(sw.Elapsed.TotalMilliseconds);
        }

        latencies.Sort();
        double At(double q) => latencies[Math.Min(latencies.Count - 1, (int)Math.Ceiling(q * latencies.Count) - 1)];
        var p50 = At(0.50);
        var p95 = At(0.95);
        var p99 = At(0.99);

        _out.WriteLine($"rows={RowCount:N0} n={Iterations}  p50={p50:F1}ms  p95={p95:F1}ms  " +
                       $"p99={p99:F1}ms  max={latencies[^1]:F1}ms");

        p95.Should().BeLessThan(P95ThresholdMs,
            $"DoD Sprint audit yêu cầu search p95 < {P95ThresholdMs}ms với 1M row. " +
            $"Đo được p50={p50:F1}ms p95={p95:F1}ms p99={p99:F1}ms");
    }

    /// <summary>
    /// Chạy đúng hình dạng truy vấn của <c>AuditSearchQueryHandler</c>: lọc → <c>COUNT</c> tổng →
    /// sắp xếp giảm dần theo <c>occurred_at</c> → phân trang. <c>COUNT</c> thường là phần đắt nhất
    /// nên không được bỏ, nếu không phép đo sẽ lạc quan hơn thực tế.
    /// </summary>
    private static async Task RunSearchAsync(AuditAggregateDbContext db, string service, string severity)
    {
        var from = DateTime.UtcNow.AddDays(-30);

        var query = db.AuditAggregates.AsNoTracking()
            .Where(x => !x.IsDeleted
                        && x.ServiceName == service
                        && x.Severity == severity
                        && x.OccurredAt >= from);

        _ = await query.CountAsync();
        _ = await query
            .OrderByDescending(x => x.OccurredAt)
            .Take(50)
            .ToListAsync();
    }
}
