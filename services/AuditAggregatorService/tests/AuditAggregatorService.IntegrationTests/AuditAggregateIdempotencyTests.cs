using AuditAggregatorService.Application.CQRS.Command.Audit;
using AuditAggregatorService.Application.CQRS.Handler.Audit;
using AuditAggregatorService.Domain.Entities;
using AuditAggregatorService.Infrastructure.Implements.Repositories;
using AuditAggregatorService.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace AuditAggregatorService.IntegrationTests;

/// <summary>
/// Integration test E2E cho AuditAggregatorService (Sprint audit #AUDIT-19) — TestContainers Postgres thật.
/// Validate: migration partitioned (fallback partitions không cần pg_partman) apply OK; idempotency
/// (1000 duplicate event → 1 row) qua UnitOfWork (pattern giống consumer); insert thật vào audit_aggregate.
/// </summary>
public class AuditAggregateIdempotencyTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("audit_aggregate_test")
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
        // #AUDIT-14 — apply migration partitioned (fallback path tạo partition tháng + DEFAULT).
        await using var db = NewContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _pg.DisposeAsync();

    private static AuditAggregate NewAggregate(Guid eventId, DateTime occurredAt) => AuditAggregate.FromEvent(
        eventId, "AuthService", "LoginSucceeded", "Authentication", "Info",
        "Account", Guid.NewGuid(), "x@example.com", Guid.NewGuid(), "Admin", "Admin User", "127.0.0.1", "ua",
        true, null, null, null, Guid.NewGuid(), null, occurredAt, DateTime.UtcNow);

    /// <summary>Insert idempotent qua UnitOfWork — mirror logic consumer (AnyAsync + AddAsync + catch unique-violation).</summary>
    private static async Task<bool> InsertIfNotExistsAsync(AuditAggregateDbContext db, AuditAggregate agg)
    {
        var uow = new UnitOfWork(db);
        if (await uow.AuditAggregates.AnyAsync(x => x.EventId == agg.EventId))
            return false;

        await uow.AuditAggregates.AddAsync(agg);
        try
        {
            await uow.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException)
        {
            db.Entry(agg).State = EntityState.Detached;
            return false;
        }
    }

    [Fact]
    public async Task Insert_DuplicateEventId_OnlyOneRow()
    {
        var eventId = Guid.NewGuid();
        var occurredAt = DateTime.UtcNow;

        // 1000 duplicate event (cùng event_id+occurred_at) → idempotent insert chỉ 1 row.
        for (int i = 0; i < 1000; i++)
        {
            await using var db = NewContext();
            await InsertIfNotExistsAsync(db, NewAggregate(eventId, occurredAt));
        }

        await using var verify = NewContext();
        var count = await verify.AuditAggregates.CountAsync(x => x.EventId == eventId);
        count.Should().Be(1, "#AUDIT-15 idempotency — 1000 duplicate event_id chỉ insert 1 row");
    }

    [Fact]
    public async Task Insert_DistinctEvents_AllPersisted_OnPartitionedTable()
    {
        var ids = Enumerable.Range(0, 20).Select(_ => Guid.NewGuid()).ToList();

        foreach (var id in ids)
        {
            await using var db = NewContext();
            var inserted = await InsertIfNotExistsAsync(db, NewAggregate(id, DateTime.UtcNow));
            inserted.Should().BeTrue();
        }

        await using var verify = NewContext();
        foreach (var id in ids)
            (await verify.AuditAggregates.AnyAsync(x => x.EventId == id)).Should().BeTrue();
    }

    [Fact]
    public async Task RedactHandler_MasksPii_KeepsRows()
    {
        var accountId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        // 2 row của account (1 actor, 1 target) + 1 row không liên quan.
        await using (var db = NewContext())
        {
            var uow = new UnitOfWork(db);
            await uow.AuditAggregates.AddAsync(AuditAggregate.FromEvent(
                Guid.NewGuid(), "AuthService", "LoginSucceeded", "Authentication", "Info",
                "Account", Guid.NewGuid(), "t@example.com", accountId, "Admin", "Tên Thật", "1.2.3.4", "ua",
                true, null, null, null, Guid.NewGuid(), null, now, now));
            await uow.AuditAggregates.AddAsync(AuditAggregate.FromEvent(
                Guid.NewGuid(), "AuthService", "AccountLocked", "AccountManagement", "Warning",
                "Account", accountId, "Tên Target", Guid.NewGuid(), "Admin", "Admin", "5.6.7.8", "ua",
                true, null, null, null, Guid.NewGuid(), null, now, now));
            await uow.AuditAggregates.AddAsync(NewAggregate(Guid.NewGuid(), now));
            await uow.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            var result = await new AuditRedactCommandHandler(new UnitOfWork(db))
                .Handle(new AuditRedactCommand { AccountId = accountId }, CancellationToken.None);
            result.IsSuccess.Should().BeTrue();
            result.StatusCode.Should().Be(200);
        }

        await using var verify = NewContext();
        var rows = await verify.AuditAggregates
            .Where(x => x.ActorAccountId == accountId || x.TargetId == accountId).ToListAsync();
        rows.Should().HaveCount(2, "redact KHÔNG xóa row");
        rows.Should().OnlyContain(x => x.ActorDisplay == "[REDACTED]" && x.ActorIp == "[REDACTED]");
        (await verify.AuditAggregates.CountAsync()).Should().Be(3, "row không liên quan vẫn còn");
    }
}
