using BatteryService.Application.Interfaces;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Interfaces.Repositories;
using SharedContracts.Events;
using SharedInfrastructure.Idempotency;
using SharedInfrastructure.Persistence.Interceptors;
using SharedInfrastructure.Services;
using Testcontainers.PostgreSql;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Enums;
using Xunit;
using BatteryDbContext = BatteryService.Infrastructure.Persistence.ApplicationDbContext;
using BatterySnapshotConsumer = BatteryService.Infrastructure.Consumers.AccountSyncSnapshotConsumer;
using BatteryUnitOfWork = BatteryService.Infrastructure.Implements.Repositories.UnitOfWork;
using NotificationDbContext = NotificationService.Infrastructure.Persistence.ApplicationDbContext;
using NotificationSnapshotConsumer = NotificationService.Application.Consumers.AccountSnapshotSyncConsumer;
using NotificationUnitOfWork = NotificationService.Infrastructure.Implements.Repositories.UnitOfWork;
using TicketDbContext = TicketService.Infrastructure.Persistence.TicketDbContext;
using TicketSnapshotConsumer = TicketService.Infrastructure.Consumers.TicketAccountSyncSnapshotConsumer;
using TicketUnitOfWork = TicketService.Infrastructure.Implements.Repositories.UnitOfWork;

namespace AccountProjection.E2ETests;

/// <summary>
/// Production-shaped account projection test: a real RabbitMQ broker fans one AuthService
/// snapshot out to three independently migrated PostgreSQL databases. A second snapshot must
/// repair deliberate database drift; a role snapshot must move Ticket's current projection and
/// repair staff-profile fields.
/// </summary>
public sealed class AccountProjectionReconciliationE2ETests : IAsyncLifetime
{
    private static readonly TimeSpan EventuallyTimeout = TimeSpan.FromSeconds(30);

    private readonly PostgreSqlContainer _batteryPostgres = NewPostgres(
        "battery_projection_e2e",
        "timescale/timescaledb:2.17.2-pg16");
    private readonly PostgreSqlContainer _ticketPostgres = NewPostgres("ticket_projection_e2e");
    private readonly PostgreSqlContainer _notificationPostgres = NewPostgres("notification_projection_e2e");
    private readonly IContainer _rabbitMq = new ContainerBuilder("rabbitmq:3.13.7-alpine")
        .WithEnvironment("RABBITMQ_DEFAULT_USER", "projection-test")
        .WithEnvironment("RABBITMQ_DEFAULT_PASS", "projection-test")
        .WithPortBinding(5672, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(5672))
        .WithCleanUp(true)
        .Build();

    private ServiceProvider? _provider;
    private IBusControl? _bus;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(
            _batteryPostgres.StartAsync(),
            _ticketPostgres.StartAsync(),
            _notificationPostgres.StartAsync(),
            _rabbitMq.StartAsync());

        await Task.WhenAll(MigrateBatteryAsync(), MigrateTicketAsync(), MigrateNotificationAsync());

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddScoped<AuditableEntityInterceptor>(_ =>
            new AuditableEntityInterceptor(new NoUserCurrentUserService()));
        services.AddDbContext<BatteryDbContext>(options =>
            options.UseNpgsql(_batteryPostgres.GetConnectionString()));
        services.AddDbContext<TicketDbContext>(options =>
            options.UseNpgsql(_ticketPostgres.GetConnectionString()));
        services.AddDbContext<NotificationDbContext>(options =>
            options.UseNpgsql(_notificationPostgres.GetConnectionString()));
        services.AddScoped<IBatteryUnitOfWork, BatteryUnitOfWork>();
        services.AddScoped<ITicketUnitOfWork, TicketUnitOfWork>();
        services.AddScoped<INotificationUnitOfWork, NotificationUnitOfWork>();
        services.AddSingleton<IInboxStore, AlwaysClaimInboxStore>();

        services.AddMassTransit(configurator =>
        {
            configurator.AddConsumer<BatterySnapshotConsumer>();
            configurator.AddConsumer<TicketSnapshotConsumer>();
            configurator.AddConsumer<NotificationSnapshotConsumer>();

            configurator.UsingRabbitMq((context, rabbit) =>
            {
                rabbit.Host(
                    new Uri($"rabbitmq://127.0.0.1:{_rabbitMq.GetMappedPublicPort(5672)}/"),
                    host =>
                    {
                        host.Username("projection-test");
                        host.Password("projection-test");
                    });

                rabbit.ReceiveEndpoint("account-projection-e2e-battery", endpoint =>
                    endpoint.ConfigureConsumer<BatterySnapshotConsumer>(context));
                rabbit.ReceiveEndpoint("account-projection-e2e-ticket", endpoint =>
                    endpoint.ConfigureConsumer<TicketSnapshotConsumer>(context));
                rabbit.ReceiveEndpoint("account-projection-e2e-notification", endpoint =>
                    endpoint.ConfigureConsumer<NotificationSnapshotConsumer>(context));
            });
        });

        _provider = services.BuildServiceProvider(validateScopes: true);
        _bus = _provider.GetRequiredService<IBusControl>();
        await _bus.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_bus is not null)
            await _bus.StopAsync();
        if (_provider is not null)
            await _provider.DisposeAsync();

        await Task.WhenAll(
            _rabbitMq.DisposeAsync().AsTask(),
            _batteryPostgres.DisposeAsync().AsTask(),
            _ticketPostgres.DisposeAsync().AsTask(),
            _notificationPostgres.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task FullSnapshot_ShouldCreateRepairAndMoveAllProductionProjections()
    {
        var accountId = Guid.NewGuid();
        var firstSnapshotAt = DateTime.UtcNow;

        await PublishAsync(new AccountSyncSnapshotEvent(
            accountId,
            "customer.initial@example.com",
            "Initial Customer",
            "+84900000001",
            "Customer",
            IsActive: true,
            IsDeleted: false,
            SnapshotAtUtc: firstSnapshotAt,
            Reason: "E2E-create",
            AccountStatus: 1));

        await EventuallyAsync(async () =>
            await HasCanonicalCustomerAsync(accountId, "customer.initial@example.com", "Initial Customer"));

        // Simulate exactly the production problem: someone changes all three projection DBs by
        // hand. AuthService remains authoritative, so the next periodic snapshot must overwrite
        // every mirrored field rather than merely insert missing rows.
        await CorruptAllCustomerProjectionsAsync(accountId);

        var repairSnapshotAt = firstSnapshotAt.AddMinutes(1);
        await PublishAsync(new AccountSyncSnapshotEvent(
            accountId,
            "customer.canonical@example.com",
            "Canonical Customer",
            "+84900000002",
            "Customer",
            IsActive: true,
            IsDeleted: false,
            SnapshotAtUtc: repairSnapshotAt,
            Reason: "E2E-repair",
            AccountStatus: 1));

        await EventuallyAsync(async () =>
            await HasCanonicalCustomerAsync(accountId, "customer.canonical@example.com", "Canonical Customer"));

        var roleSnapshotAt = repairSnapshotAt.AddMinutes(1);
        await PublishAsync(new AccountSyncSnapshotEvent(
            accountId,
            "staff.canonical@example.com",
            "Canonical Staff",
            "+84900000003",
            "Staff",
            IsActive: true,
            IsDeleted: false,
            SnapshotAtUtc: roleSnapshotAt,
            Reason: "E2E-role",
            AccountStatus: 1,
            HasStaffProfileSnapshot: true,
            EmployeeCode: "STAFF-E2E-001",
            MaxConcurrentTickets: 7,
            IsAvailable: false,
            SkillTier: (int)StaffSkillTierEnum.SeniorSpecialist,
            SkillCodes: new List<string> { "ELEC", "SOLAR" }));

        await EventuallyAsync(async () => await HasCanonicalStaffRoleAsync(accountId));

        var deleteSnapshotAt = roleSnapshotAt.AddMinutes(1);
        await PublishAsync(new AccountSyncSnapshotEvent(
            accountId,
            "staff.canonical@example.com",
            "Canonical Staff",
            "+84900000003",
            "Staff",
            IsActive: false,
            IsDeleted: true,
            SnapshotAtUtc: deleteSnapshotAt,
            Reason: "E2E-delete",
            AccountStatus: 3));

        await EventuallyAsync(async () => await AllProjectionsAreSoftDeletedAsync(accountId));
    }

    private async Task PublishAsync(AccountSyncSnapshotEvent snapshot)
    {
        await _bus!.Publish(snapshot);
    }

    private async Task<bool> HasCanonicalCustomerAsync(Guid accountId, string email, string fullName)
    {
        await using var battery = NewBatteryContext();
        await using var ticket = NewTicketContext();
        await using var notification = NewNotificationContext();

        var batteryAccount = await battery.CustomerAccounts.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == accountId);
        var ticketAccount = await ticket.CustomerAccounts.AsNoTracking()
            .SingleOrDefaultAsync(item => item.AccountId == accountId);
        var notificationAccount = await notification.AccountReadModels.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == accountId);

        return batteryAccount is
        {
            Role: "Customer",
            IsActive: true,
            IsDeleted: false
        }
               && batteryAccount.Email == email
               && batteryAccount.FullName == fullName
               && ticketAccount is
               {
                   Status: AccountStatusEnum.Active,
                   IsDeleted: false
               }
               && ticketAccount.Email == email
               && ticketAccount.FullName == fullName
               && notificationAccount is
               {
                   Role: "Customer",
                   IsActive: true,
                   IsDeleted: false
               }
               && notificationAccount.Email == email
               && notificationAccount.FullName == fullName;
    }

    private async Task CorruptAllCustomerProjectionsAsync(Guid accountId)
    {
        await using (var battery = NewBatteryContext())
        {
            var row = await battery.CustomerAccounts.SingleAsync(item => item.Id == accountId);
            row.Email = "manual-drift@invalid.local";
            row.FullName = "Manual drift";
            row.Role = "Admin";
            row.IsActive = false;
            await battery.SaveChangesAsync();
        }

        await using (var ticket = NewTicketContext())
        {
            var row = await ticket.CustomerAccounts.SingleAsync(item => item.AccountId == accountId);
            row.Email = "manual-drift@invalid.local";
            row.FullName = "Manual drift";
            row.Status = AccountStatusEnum.Banned;
            await ticket.SaveChangesAsync();
        }

        await using (var notification = NewNotificationContext())
        {
            var row = await notification.AccountReadModels.SingleAsync(item => item.Id == accountId);
            row.Email = "manual-drift@invalid.local";
            row.FullName = "Manual drift";
            row.Role = "Admin";
            row.IsActive = false;
            await notification.SaveChangesAsync();
        }
    }

    private async Task<bool> HasCanonicalStaffRoleAsync(Guid accountId)
    {
        await using var battery = NewBatteryContext();
        await using var ticket = NewTicketContext();
        await using var notification = NewNotificationContext();

        var batteryAccount = await battery.CustomerAccounts.AsNoTracking()
            .SingleAsync(item => item.Id == accountId);
        var ticketCustomer = await ticket.CustomerAccounts.AsNoTracking()
            .SingleAsync(item => item.AccountId == accountId);
        var ticketStaff = await ticket.StaffAccounts.AsNoTracking()
            .SingleOrDefaultAsync(item => item.AccountId == accountId);
        var notificationAccount = await notification.AccountReadModels.AsNoTracking()
            .SingleAsync(item => item.Id == accountId);

        return batteryAccount.Role == "Staff"
               && !batteryAccount.IsActive
               && ticketCustomer.Status == AccountStatusEnum.Inactive
               && ticketStaff is
               {
                   Role: "Staff",
                   Status: AccountStatusEnum.Active,
                   EmployeeCode: "STAFF-E2E-001",
                   MaxConcurrentTickets: 7,
                   IsAvailable: false,
                   SkillTier: StaffSkillTierEnum.SeniorSpecialist
               }
               && ticketStaff.SkillCodes.SequenceEqual(new[] { "ELEC", "SOLAR" })
               && notificationAccount.Role == "Staff"
               && notificationAccount.IsActive;
    }

    private async Task<bool> AllProjectionsAreSoftDeletedAsync(Guid accountId)
    {
        await using var battery = NewBatteryContext();
        await using var ticket = NewTicketContext();
        await using var notification = NewNotificationContext();

        var batteryAccount = await battery.CustomerAccounts.AsNoTracking()
            .SingleAsync(item => item.Id == accountId);
        var ticketCustomer = await ticket.CustomerAccounts.AsNoTracking()
            .SingleAsync(item => item.AccountId == accountId);
        var ticketStaff = await ticket.StaffAccounts.AsNoTracking()
            .SingleAsync(item => item.AccountId == accountId);
        var notificationAccount = await notification.AccountReadModels.AsNoTracking()
            .SingleAsync(item => item.Id == accountId);

        return batteryAccount.IsDeleted
               && !batteryAccount.IsActive
               && ticketCustomer.IsDeleted
               && ticketCustomer.Status == AccountStatusEnum.Inactive
               && ticketStaff.IsDeleted
               && ticketStaff.Status == AccountStatusEnum.Inactive
               && notificationAccount.IsDeleted
               && !notificationAccount.IsActive;
    }

    private static async Task EventuallyAsync(Func<Task<bool>> assertion)
    {
        var deadline = DateTime.UtcNow + EventuallyTimeout;
        Exception? lastError = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (await assertion())
                    return;
            }
            catch (Exception exception)
            {
                lastError = exception;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException("Account projections did not converge within 30 seconds.", lastError);
    }

    private async Task MigrateBatteryAsync()
    {
        await using var context = NewBatteryContext();
        await context.Database.MigrateAsync();
        await context.GetService<IMigrator>()
            .MigrateAsync("20260826033548_AddMaintenanceCycleSnapshot");
        await context.Database.MigrateAsync();
    }

    private async Task MigrateTicketAsync()
    {
        await using var context = NewTicketContext();
        await context.Database.MigrateAsync();
        await context.GetService<IMigrator>()
            .MigrateAsync("20260826123257_DropPeriodicMaintenanceSourceTicketId");
        await context.Database.MigrateAsync();
    }

    private async Task MigrateNotificationAsync()
    {
        await using var context = NewNotificationContext();
        await context.Database.MigrateAsync();
    }

    private BatteryDbContext NewBatteryContext()
        => new(
            new DbContextOptionsBuilder<BatteryDbContext>()
                .UseNpgsql(_batteryPostgres.GetConnectionString())
                .Options,
            NewAuditInterceptor());

    private TicketDbContext NewTicketContext()
        => new(
            new DbContextOptionsBuilder<TicketDbContext>()
                .UseNpgsql(_ticketPostgres.GetConnectionString())
                .Options,
            NewAuditInterceptor());

    private NotificationDbContext NewNotificationContext()
        => new(
            new DbContextOptionsBuilder<NotificationDbContext>()
                .UseNpgsql(_notificationPostgres.GetConnectionString())
                .Options,
            NewAuditInterceptor());

    private static AuditableEntityInterceptor NewAuditInterceptor()
        => new(new NoUserCurrentUserService());

    private static PostgreSqlContainer NewPostgres(
        string database,
        string image = "postgres:16-alpine")
        => new PostgreSqlBuilder(image)
            .WithDatabase(database)
            .WithUsername("projection-test")
            .WithPassword("projection-test")
            .WithCleanUp(true)
            .Build();

    private sealed class NoUserCurrentUserService : ICurrentUserService
    {
        public string? UserId => null;
    }

    private sealed class AlwaysClaimInboxStore : IInboxStore
    {
        public Task<InboxClaim> TryBeginAsync(
            Guid messageId,
            string consumerName,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new InboxClaim(InboxClaimStatus.Claimed, Guid.NewGuid().ToString("N")));

        public Task CompleteAsync(
            Guid messageId,
            string consumerName,
            string token,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ReleaseAsync(
            Guid messageId,
            string consumerName,
            string token,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
