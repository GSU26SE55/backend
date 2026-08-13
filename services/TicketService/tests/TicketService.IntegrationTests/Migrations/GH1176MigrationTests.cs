using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Moq;
using SharedInfrastructure.Persistence.Interceptors;
using SharedInfrastructure.Services;
using Testcontainers.PostgreSql;
using TicketService.Domain.Entities;
using TicketService.Infrastructure.Persistence;

namespace TicketService.IntegrationTests.Migrations;

[Trait("Category", "Migration")]
public class GH1176MigrationTests : IAsyncLifetime
{
    private const string MigrationId = "20260811033315_GH1176ReviseTicketStatusSlaWorkflow";
    private const string PreviousMigrationId = "20260808054339_AddTicketAiSuggestionAndStaffRole";

    [Obsolete]
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("ticket_migration")
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
    public async Task ApplyMigrations_CreatesGh1176ScheduleAndIncidentSchema()
    {
        await using var db = CreateDbContext();

        await db.Database.MigrateAsync();

        var appliedMigrations = await db.Database.GetAppliedMigrationsAsync();
        appliedMigrations.Should().Contain(MigrationId);

        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var columnsCommand = connection.CreateCommand();
        columnsCommand.CommandText = """
            SELECT COUNT(*)
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = 'tickets'
              AND column_name IN (
                  'active_incident_episode_id',
                  'pending_context',
                  'pending_reason',
                  'schedule_version',
                  'scheduled_start_at_utc');
            """;

        Convert.ToInt32(await columnsCommand.ExecuteScalarAsync()).Should().Be(5);

        await using var indexCommand = connection.CreateCommand();
        indexCommand.CommandText = """
            SELECT COUNT(*)
            FROM pg_indexes
            WHERE schemaname = 'public'
              AND tablename = 'tickets'
              AND indexname = 'ix_tickets_due_activation';
            """;

        Convert.ToInt32(await indexCommand.ExecuteScalarAsync()).Should().Be(1);
    }

    [Fact]
    [Obsolete]
    public async Task OutboxPrimaryKey_RejectsDuplicateDeterministicEventId()
    {
        var eventId = Guid.NewGuid();
        await using (var first = CreateDbContext())
        {
            await first.Database.MigrateAsync();
            first.OutboxMessages.Add(new OutboxMessage
            {
                Id = eventId,
                AggregateId = eventId,
                Type = "BatteryIsolationRequestedEvent",
                Payload = "{}",
                OccurredAtUtc = DateTime.UtcNow
            });
            await first.SaveChangesAsync();
        }

        await using var second = CreateDbContext();
        second.OutboxMessages.Add(new OutboxMessage
        {
            Id = eventId,
            AggregateId = eventId,
            Type = "BatteryIsolationRequestedEvent",
            Payload = "{}",
            OccurredAtUtc = DateTime.UtcNow
        });

        var saveDuplicate = () => second.SaveChangesAsync();
        await saveDuplicate.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    [Obsolete]
    public async Task ApplyMigration_RemapLegacySlaPauseReasons()
    {
        await using var db = CreateDbContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigrationId);

        var ticketId = Guid.NewGuid();
        var timerId = Guid.NewGuid();
        var waitingOnsiteId = Guid.NewGuid();
        var awaitingChatId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var nowUtc = DateTime.UtcNow;

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO tickets
                (id, code, battery_asset_id, customer_id, title, description, category,
                 priority, status, origin, reopen_count, is_incident, ai_verify_status,
                 created_at, is_deleted)
            VALUES
                ({ticketId}, 'T-MIGRATION', {Guid.NewGuid()}, {Guid.NewGuid()},
                 'Migration ticket', 'Migration ticket', 1, 1, 5, 1, 0, FALSE, 1,
                 {nowUtc}, FALSE);

            INSERT INTO sla_timers
                (id, ticket_id, priority, started_at, due_at, original_due_at,
                 total_paused_minutes, status, max_total_pause_minutes,
                 max_pause_episodes, pause_episodes_count, approval_required)
            VALUES
                ({timerId}, {ticketId}, 1, {nowUtc}, {nowUtc.AddHours(8)}, {nowUtc.AddHours(8)},
                 0, 2, 0, 0, 1, FALSE);

            INSERT INTO sla_pause_events
                (id, sla_timer_id, reason, paused_at, paused_by_user_id, created_at, is_deleted)
            VALUES
                ({waitingOnsiteId}, {timerId}, 3, {nowUtc}, {actorId}, {nowUtc}, FALSE),
                ({awaitingChatId}, {timerId}, 4, {nowUtc}, {actorId}, {nowUtc}, FALSE);
            """);

        await migrator.MigrateAsync(MigrationId);

        await using var command = db.Database.GetDbConnection().CreateCommand();
        if (command.Connection!.State != System.Data.ConnectionState.Open)
            await command.Connection.OpenAsync();
        command.CommandText = """
            SELECT COUNT(*) FILTER (WHERE reason = 2),
                   COUNT(*) FILTER (WHERE reason = 1),
                   COUNT(*) FILTER (WHERE reason IN (3, 4))
            FROM sla_pause_events
            WHERE id IN (@waitingOnsiteId, @awaitingChatId);
            """;
        var first = command.CreateParameter();
        first.ParameterName = "waitingOnsiteId";
        first.Value = waitingOnsiteId;
        command.Parameters.Add(first);
        var second = command.CreateParameter();
        second.ParameterName = "awaitingChatId";
        second.Value = awaitingChatId;
        command.Parameters.Add(second);

        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        reader.GetInt64(0).Should().Be(1);
        reader.GetInt64(1).Should().Be(1);
        reader.GetInt64(2).Should().Be(0);
    }

    [Obsolete]
    private TicketDbContext CreateDbContext()
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(x => x.UserId).Returns((string?)null);

        var options = new DbContextOptionsBuilder<TicketDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        return new TicketDbContext(options, new AuditableEntityInterceptor(currentUser.Object));
    }
}
