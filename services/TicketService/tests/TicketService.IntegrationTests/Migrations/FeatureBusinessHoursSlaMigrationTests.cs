using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Moq;
using Npgsql;
using SharedInfrastructure.Persistence.Interceptors;
using SharedInfrastructure.Services;
using Testcontainers.PostgreSql;
using TicketService.Infrastructure.Persistence;

namespace TicketService.IntegrationTests.Migrations;

[Trait("Category", "Migration")]
public sealed class FeatureBusinessHoursSlaMigrationTests : IAsyncLifetime
{
    private const string MigrationId =
        "20260817054403_FEATUREBusinessHoursSlaAddNonWorkingPeriods";
    private const string PreviousMigrationId =
        "20260811033315_GH1176ReviseTicketStatusSlaWorkflow";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("ticket_sla_calendar_migration")
        .WithUsername("test")
        .WithPassword("test")
        .WithCleanUp(true)
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task ApplyAndRollback_CreatesAndRemovesNonWorkingPeriodSchema()
    {
        await using var db = CreateDbContext();
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync(MigrationId);
        (await db.Database.GetAppliedMigrationsAsync()).Should().Contain(MigrationId);

        await using (var columnsCommand = db.Database.GetDbConnection().CreateCommand())
        {
            await db.Database.OpenConnectionAsync();
            columnsCommand.CommandText = """
                SELECT COUNT(*)
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = 'sla_non_working_periods'
                  AND column_name IN (
                      'id', 'start_date', 'end_date', 'reason', 'created_at',
                      'created_by', 'updated_at', 'is_deleted', 'deleted_at');
                """;

            Convert.ToInt32(await columnsCommand.ExecuteScalarAsync()).Should().Be(9);
        }

        await using (var indexesCommand = db.Database.GetDbConnection().CreateCommand())
        {
            indexesCommand.CommandText = """
                SELECT COUNT(*)
                FROM pg_indexes
                WHERE schemaname = 'public'
                  AND tablename = 'sla_non_working_periods'
                  AND indexname IN (
                      'IX_sla_non_working_periods_is_deleted',
                      'IX_sla_non_working_periods_start_date_end_date');
                """;

            Convert.ToInt32(await indexesCommand.ExecuteScalarAsync()).Should().Be(2);
        }

        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO sla_non_working_periods
                (id, start_date, end_date, reason, created_at, is_deleted)
            VALUES
                ('00000000-0000-0000-0000-000000000101', DATE '2026-09-01', DATE '2026-09-03',
                 'First range', NOW(), FALSE);
            """);

        var insertOverlap = () => db.Database.ExecuteSqlRawAsync("""
            INSERT INTO sla_non_working_periods
                (id, start_date, end_date, reason, created_at, is_deleted)
            VALUES
                ('00000000-0000-0000-0000-000000000102', DATE '2026-09-03', DATE '2026-09-05',
                 'Overlapping range', NOW(), FALSE);
            """);

        var overlapError = await insertOverlap.Should().ThrowAsync<PostgresException>();
        overlapError.Which.SqlState.Should().Be(PostgresErrorCodes.ExclusionViolation);
        overlapError.Which.ConstraintName.Should()
            .Be("ex_sla_non_working_periods_no_active_overlap");

        await migrator.MigrateAsync(PreviousMigrationId);

        await using var rollbackCommand = db.Database.GetDbConnection().CreateCommand();
        rollbackCommand.CommandText =
            "SELECT to_regclass('public.sla_non_working_periods') IS NULL;";
        Convert.ToBoolean(await rollbackCommand.ExecuteScalarAsync()).Should().BeTrue();
    }

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
