using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using SharedInfrastructure.Persistence.Interceptors;
using SharedInfrastructure.Services;
using Testcontainers.PostgreSql;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.Persistence;

namespace TicketService.IntegrationTests.Persistence;

public class AccountStatusStorageCompatibilityTests : IAsyncLifetime
{
    [Obsolete]
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("ticket_account_status_storage")
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
    public async Task ExistingAndNewRows_RoundTripWithoutChangingLegacyDatabaseContract()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();

        var existingCustomerId = Guid.NewGuid();
        var existingStaffId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO customer_accounts
                (id, account_id, email, full_name, status, last_synced_at, created_at, is_deleted)
            VALUES
                ({existingCustomerId}, {existingCustomerId}, 'active-customer@test.local',
                 'Active customer', 2, {now}, {now}, FALSE);

            INSERT INTO staff_accounts
                (id, account_id, email, full_name, status, is_available,
                 max_concurrent_tickets, skill_codes, last_synced_at, created_at, is_deleted)
            VALUES
                ({existingStaffId}, {existingStaffId}, 'locked-staff@test.local',
                 'Locked staff', 3, TRUE, 3, '[]'::jsonb, {now}, {now}, FALSE);
            """);

        (await db.CustomerAccounts.SingleAsync(account => account.Id == existingCustomerId))
            .Status.Should().Be(AccountStatusEnum.Active);
        (await db.StaffAccounts.SingleAsync(account => account.Id == existingStaffId))
            .Status.Should().Be(AccountStatusEnum.Locked);

        var newCustomerId = Guid.NewGuid();
        await db.CustomerAccounts.AddAsync(new CustomerAccount
        {
            Id = newCustomerId,
            AccountId = newCustomerId,
            Email = "suspended-customer@test.local",
            FullName = "Suspended customer",
            Status = AccountStatusEnum.Suspended,
            LastSyncedAt = now,
        });
        await db.SaveChangesAsync();

        (await ReadStoredStatusAsync(db, "customer_accounts", newCustomerId)).Should().Be(5);
    }

    private static async Task<int> ReadStoredStatusAsync(TicketDbContext db, string table, Guid id)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"SELECT status FROM {table} WHERE id = @id";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "id";
        parameter.Value = id;
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
