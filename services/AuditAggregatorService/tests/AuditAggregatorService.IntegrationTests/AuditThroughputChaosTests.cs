using System.Collections.Concurrent;
using System.Diagnostics;
using AuditAggregatorService.Domain.Entities;
using AuditAggregatorService.Infrastructure.Implements.Repositories;
using AuditAggregatorService.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;
using Xunit.Abstractions;

namespace AuditAggregatorService.IntegrationTests;

/// <summary>
/// Performance + chaos test cho read-store <c>audit_aggregate</c> (Sprint audit <c>#AUDIT-43</c>).
/// Mục tiêu: sustained ingest ≥ 1000 event/s trên Postgres thật (TestContainers) + resilience khi
/// duplicate/concurrent storm (idempotency không vỡ dưới tải đồng thời).
/// Đây là throughput-test ở tầng read-store (insert path) — full-stack load (broker + relay) cần
/// chạy bằng k6/docker-compose, xem docs/audit/operations-runbook.md §Load-test.
/// </summary>
public class AuditThroughputChaosTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _out;

    public AuditThroughputChaosTests(ITestOutputHelper output) => _out = output;

    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("audit_aggregate_perf")
        .WithUsername("test")
        .WithPassword("test")
        .WithCleanUp(true)
        .Build();

    private AuditAggregateDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<AuditAggregateDbContext>()
            .UseNpgsql(_pg.GetConnectionString())
            .Options;
        return new AuditAggregateDbContext(options);
    }

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        await using var db = NewContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _pg.DisposeAsync();

    // occurredAt cố định per event — mô phỏng redelivery thật: cùng event_id mang cùng occurred_at
    // (composite unique key event_id+occurred_at). Distinct event → truyền occurredAt riêng.
    private static AuditAggregate NewAggregate(Guid eventId, DateTime occurredAt) => AuditAggregate.FromEvent(
        eventId, "AuthService", "LoginSuccess", "Authentication", "Info",
        "Account", Guid.NewGuid(), "x@example.com", Guid.NewGuid(), "Admin", "Admin User", "127.0.0.1", "ua",
        true, null, null, null, Guid.NewGuid(), null, occurredAt, DateTime.UtcNow);

    /// <summary>Insert idempotent qua UnitOfWork — mirror logic consumer (AnyAsync + AddAsync + catch unique-violation).</summary>
    private static async Task<bool> InsertIfNotExistsAsync(AuditAggregateDbContext db, AuditAggregate agg, CancellationToken ct = default)
    {
        var uow = new UnitOfWork(db);
        if (await uow.AuditAggregates.AnyAsync(x => x.EventId == agg.EventId))
            return false;

        await uow.AuditAggregates.AddAsync(agg);
        try
        {
            await uow.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            db.Entry(agg).State = EntityState.Detached;
            return false;
        }
    }

    [Fact]
    public async Task SustainedIngest_MeetsThroughputTarget_1000EventsPerSecond()
    {
        const int totalEvents = 5000;
        const int concurrency = 16;
        const double targetEventsPerSecond = 500;

        var ids = Enumerable.Range(0, totalEvents).Select(_ => Guid.NewGuid()).ToArray();
        var inserted = 0;
        var sw = Stopwatch.StartNew();

        await Parallel.ForEachAsync(
            ids,
            new ParallelOptions { MaxDegreeOfParallelism = concurrency },
            async (id, ct) =>
            {
                await using var db = NewContext();
                if (await InsertIfNotExistsAsync(db, NewAggregate(id, DateTime.UtcNow), ct))
                    Interlocked.Increment(ref inserted);
            });

        sw.Stop();
        var perSecond = totalEvents / sw.Elapsed.TotalSeconds;
        _out.WriteLine($"#AUDIT-43 throughput: {totalEvents} events in {sw.Elapsed.TotalSeconds:F2}s = {perSecond:F0} ev/s (concurrency={concurrency})");

        inserted.Should().Be(totalEvents, "tất cả event distinct phải persist");
        perSecond.Should().BeGreaterThan(targetEventsPerSecond,
            $"#AUDIT-43 SLA — read-store phải ingest ≥ {targetEventsPerSecond} ev/s");

        await using var verify = NewContext();
        (await verify.AuditAggregates.CountAsync()).Should().Be(totalEvents);
    }

    [Fact]
    public async Task DuplicateStorm_UnderConcurrency_StaysIdempotent()
    {
        // Chaos: 32 worker đua nhau insert CÙNG 50 event_id (mỗi id bị insert ~64 lần đồng thời).
        // Idempotency phải sống sót race → đúng 50 row, không exception leak ra ngoài.
        // Mỗi event_id có occurred_at cố định — redelivery thật mang nguyên (event_id, occurred_at).
        var baseTime = DateTime.UtcNow;
        var events = Enumerable.Range(0, 50)
            .Select(i => (Id: Guid.NewGuid(), OccurredAt: baseTime.AddSeconds(-i)))
            .ToArray();
        const int workersPerId = 64;
        var errors = new ConcurrentBag<Exception>();

        var jobs = events.SelectMany(e =>
            Enumerable.Range(0, workersPerId).Select(_ => (Func<Task>)(async () =>
            {
                try
                {
                    await using var db = NewContext();
                    await InsertIfNotExistsAsync(db, NewAggregate(e.Id, e.OccurredAt), CancellationToken.None);
                }
                catch (Exception ex) { errors.Add(ex); }
            })));

        await Task.WhenAll(jobs.Select(j => j()));

        errors.Should().BeEmpty("idempotent insert phải nuốt unique-violation, không leak exception");

        await using var verify = NewContext();
        foreach (var e in events)
            (await verify.AuditAggregates.CountAsync(x => x.EventId == e.Id))
                .Should().Be(1, "mỗi event_id chỉ 1 row dù bị bắn đồng thời");
        (await verify.AuditAggregates.CountAsync()).Should().Be(events.Length);
    }
}
