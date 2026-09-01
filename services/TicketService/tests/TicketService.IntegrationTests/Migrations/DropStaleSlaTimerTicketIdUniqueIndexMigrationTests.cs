using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Npgsql;
using SharedInfrastructure.Persistence.Interceptors;
using SharedInfrastructure.Services;
using Testcontainers.PostgreSql;
using TicketService.Infrastructure.Persistence;

namespace TicketService.IntegrationTests.Migrations;

/// <summary>
/// 20260831000000_SplitSlaTimerByType introduced a second SlaTimer per ticket (Response +
/// Resolution) but the older per-ticket UNIQUE index "IX_sla_timers_ticket_id" was left in place,
/// so inserting the Resolution timer failed with
///   23505 duplicate key value violates unique constraint "IX_sla_timers_ticket_id".
/// 20260901120000_DropStaleSlaTimerTicketIdUniqueIndex demotes that index to non-unique.
/// These tests pin both the index shape and the behaviour it unblocked.
/// </summary>
[Trait("Category", "Migration")]
public class DropStaleSlaTimerTicketIdUniqueIndexMigrationTests : IAsyncLifetime
{
    [Obsolete]
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("ticket_sla_timer_index")
        .WithUsername("test")
        .WithPassword("test")
        .WithCleanUp(true)
        .Build();

    [Obsolete]
    public Task InitializeAsync() => _postgres.StartAsync();

    [Obsolete]
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    [Obsolete]
    public async Task ApplyMigrations_SlaTimerTicketIdIndexIsNotUnique()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();

        await using var command = db.Database.GetDbConnection().CreateCommand();
        if (command.Connection!.State != System.Data.ConnectionState.Open)
            await command.Connection.OpenAsync();
        command.CommandText = """
            SELECT indexdef
            FROM pg_indexes
            WHERE schemaname = 'public'
              AND indexname = 'IX_sla_timers_ticket_id';
            """;
        var definition = (string?)await command.ExecuteScalarAsync();

        definition.Should().NotBeNull("the index must still exist for lookup performance");
        definition.Should().NotContain("UNIQUE",
            "a UNIQUE index on ticket_id alone forbids the Response+Resolution timer split");

        // The (ticket_id, type) uniqueness is the one that must survive.
        command.CommandText = """
            SELECT indexdef
            FROM pg_indexes
            WHERE schemaname = 'public'
              AND indexname = 'ux_sla_timers_ticket_type';
            """;
        var compositeDef = (string?)await command.ExecuteScalarAsync();
        compositeDef.Should().NotBeNull();
        compositeDef.Should().Contain("UNIQUE");
    }

    [Fact]
    [Obsolete]
    public async Task AfterMigrations_TicketCanHaveBothResponseAndResolutionTimers()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();

        var ticketId = Guid.NewGuid();
        var responseTimerId = Guid.NewGuid();
        var resolutionTimerId = Guid.NewGuid();
        var nowUtc = DateTime.UtcNow;

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO tickets
                (id, code, battery_asset_id, customer_id, title, description, category,
                 priority, status, origin, reopen_count, is_incident, ai_verify_status,
                 created_at, is_deleted)
            VALUES
                ({ticketId}, 'T-SLA-INDEX', {Guid.NewGuid()}, {Guid.NewGuid()},
                 'SLA index ticket', 'SLA index ticket', 1, 1, 3, 1, 0, FALSE, 1,
                 {nowUtc}, FALSE);

            INSERT INTO sla_timers
                (id, ticket_id, priority, type, started_at, due_at, original_due_at,
                 total_paused_minutes, status, max_total_pause_minutes,
                 max_pause_episodes, pause_episodes_count, approval_required)
            VALUES
                ({responseTimerId}, {ticketId}, 1, 1, {nowUtc}, {nowUtc.AddHours(4)},
                 {nowUtc.AddHours(4)}, 0, 3, 0, 0, 0, FALSE);
            """);

        // The second (Resolution) timer for the same ticket — this is exactly the insert that
        // used to blow up with 23505 on IX_sla_timers_ticket_id.
        var insertResolution = () => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO sla_timers
                (id, ticket_id, priority, type, started_at, due_at, original_due_at,
                 total_paused_minutes, status, max_total_pause_minutes,
                 max_pause_episodes, pause_episodes_count, approval_required)
            VALUES
                ({resolutionTimerId}, {ticketId}, 1, 2, {nowUtc}, {nowUtc.AddHours(8)},
                 {nowUtc.AddHours(8)}, 0, 1, 0, 0, 0, FALSE);
            """);
        await insertResolution.Should().NotThrowAsync();

        // A third timer that duplicates (ticket_id, type) must still be rejected.
        var insertDuplicateType = () => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO sla_timers
                (id, ticket_id, priority, type, started_at, due_at, original_due_at,
                 total_paused_minutes, status, max_total_pause_minutes,
                 max_pause_episodes, pause_episodes_count, approval_required)
            VALUES
                ({Guid.NewGuid()}, {ticketId}, 1, 2, {nowUtc}, {nowUtc.AddHours(8)},
                 {nowUtc.AddHours(8)}, 0, 1, 0, 0, 0, FALSE);
            """);
        (await insertDuplicateType.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    [Obsolete]
    private TicketDbContext CreateDbContext()
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(x => x.UserId).Returns((string?)null);
        var options = new DbContextOptionsBuilder<TicketDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new TicketDbContext(
            options,
            new AuditableEntityInterceptor(currentUser.Object));
    }
}
