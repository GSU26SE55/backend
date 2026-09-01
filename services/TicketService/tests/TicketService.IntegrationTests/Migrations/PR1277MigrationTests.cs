using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Moq;
using SharedInfrastructure.Persistence.Interceptors;
using SharedInfrastructure.Services;
using Testcontainers.PostgreSql;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.Persistence;

namespace TicketService.IntegrationTests.Migrations;

[Trait("Category", "Migration")]
public class PR1277MigrationTests : IAsyncLifetime
{
    private const string PreviousMigrationId = "20260827081851_AddAccountProjectionSourceVersions";
    private const string AvatarMigrationId = "20260829120000_AddAccountAvatarUrl";
    private const string OriginMigrationId = "20260829130000_MergeCreatedByStaffOriginIntoManual";

    [Obsolete]
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("ticket_pr1277")
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
    public async Task ApplyAndRollback_MigratesLegacyOriginAndAvatarColumnsSafely()
    {
        await using var db = CreateDbContext();
        var migrator = db.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigrationId);

        var ticketId = Guid.NewGuid();
        var code = $"MIG-{ticketId:N}"[..20];
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO tickets (id, code, battery_asset_id, customer_id, title, description, category, status, origin, reopen_count, is_incident, schedule_version, created_at, is_deleted) VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, 0, false, 0, {9}, false);",
            ticketId, code, Guid.NewGuid(), Guid.NewGuid(), "Legacy staff-created ticket", "Migration verification", (int)TicketCategoryEnum.Other, (int)TicketStatusEnum.Pending, 3, DateTime.UtcNow);

        await migrator.MigrateAsync(OriginMigrationId);
        db.ChangeTracker.Clear();

        (await db.Tickets.Where(ticket => ticket.Id == ticketId).Select(ticket => ticket.Origin).SingleAsync())
            .Should().Be(TicketOriginEnum.ManualByCustomer);
        (await db.Database.GetAppliedMigrationsAsync())
            .Should().Contain([AvatarMigrationId, OriginMigrationId]);
        (await CountAvatarColumnsAsync(db)).Should().Be(2);

        // Rollback hai migration mới không được lỗi. Data origin đã gộp là bất khả nghịch nên
        // giữ nguyên =1; riêng hai cột nullable được gỡ sạch khi quay về migration trước.
        await migrator.MigrateAsync(PreviousMigrationId);
        (await CountAvatarColumnsAsync(db)).Should().Be(0);
        (await ReadOriginAsync(db, ticketId)).Should().Be(1);
    }

    private static async Task<long> CountAvatarColumnsAsync(TicketDbContext db)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name IN ('customer_accounts', 'staff_accounts')
              AND column_name = 'avatar_url';
            """;
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<int> ReadOriginAsync(TicketDbContext db, Guid ticketId)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT origin FROM tickets WHERE id = @id;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "id";
        parameter.Value = ticketId;
        command.Parameters.Add(parameter);
        return (int)(await command.ExecuteScalarAsync())!;
    }

    [Obsolete]
    private TicketDbContext CreateDbContext()
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(service => service.UserId).Returns((string?)null);
        var options = new DbContextOptionsBuilder<TicketDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new TicketDbContext(
            options,
            new AuditableEntityInterceptor(currentUser.Object));
    }
}
