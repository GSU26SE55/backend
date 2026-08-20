using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using SharedInfrastructure.Persistence.Interceptors;
using SharedInfrastructure.Services;
using Testcontainers.PostgreSql;
using TicketService.Infrastructure.Persistence;

namespace TicketService.IntegrationTests.Migrations;

[Trait("Category", "Migration")]
public class GH1244MigrationTests : IAsyncLifetime
{
    private const string MigrationId = "20260820080116_GH1244PeriodicMaintenance";

    [Obsolete]
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("ticket_gh1244")
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
    public async Task ApplyMigrations_CreatesFilteredUniquePeriodicMaintenanceIndex()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();

        var applied = await db.Database.GetAppliedMigrationsAsync();
        applied.Should().Contain(MigrationId);

        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT indexdef
            FROM pg_indexes
            WHERE schemaname = 'public'
              AND indexname = 'ux_tickets_periodic_maintenance_battery_due';
            """;
        var definition = (string?)await command.ExecuteScalarAsync();

        definition.Should().NotBeNull();
        definition.Should().Contain("UNIQUE INDEX");
        definition.Should().Contain("battery_asset_id");
        definition.Should().Contain("periodic_maintenance_due_at_utc");
        definition.Should().Contain("is_deleted = false");
        definition.Should().Contain("periodic_maintenance_due_at_utc IS NOT NULL");
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
