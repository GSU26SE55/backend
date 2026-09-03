using System.Collections.Concurrent;
using BatteryService.Application.Common.Models;
using BatteryService.Application.CQRS.Command.BatteryAsset;
using BatteryService.Application.CQRS.Command.SensorReading;
using BatteryService.Application.CQRS.Handler.BatteryAsset;
using BatteryService.Application.CQRS.Handler.SensorReading;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.Infrastructure.Consumers;
using BatteryService.Infrastructure.Implements.Repositories;
using BatteryService.Infrastructure.Persistence;
using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using SharedContracts.Events;
using SharedInfrastructure.Idempotency;
using SharedInfrastructure.Persistence.Interceptors;
using SharedInfrastructure.Services;

namespace BatteryService.IntegrationTests.Application;

public class BatteryServiceIntegrationTests
{
    private static readonly IOptions<MaintenanceScheduleOptions> MaintenanceOptions =
        Options.Create(new MaintenanceScheduleOptions());

    [Fact]
    public async Task AccountActivatedConsumer_CustomerRole_UpsertsCustomerReadModelOnce()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);
        var inboxStore = new InMemoryInboxStore();
        var consumer = new BatteryAccountActivatedConsumer(unitOfWork, inboxStore);
        var customerId = Guid.NewGuid();
        var evt = new AccountActivatedEvent(
            customerId,
            "CUSTOMER@EXAMPLE.COM",
            "  Nguyen Van A  ",
            " 0900000001 ",
            "Customer",
            "integration-test");

        var consumeContext = new Mock<ConsumeContext<AccountActivatedEvent>>();
        consumeContext.SetupGet(context => context.Message).Returns(evt);
        consumeContext.SetupGet(context => context.CancellationToken).Returns(CancellationToken.None);

        await consumer.Consume(consumeContext.Object);
        await consumer.Consume(consumeContext.Object);

        var accounts = await dbContext.CustomerAccounts.ToListAsync();
        accounts.Should().ContainSingle();
        accounts[0].Id.Should().Be(customerId);
        accounts[0].Email.Should().Be("customer@example.com");
        accounts[0].FullName.Should().Be("Nguyen Van A");
        accounts[0].PhoneNumber.Should().Be("0900000001");
        accounts[0].Role.Should().Be("Customer");
        accounts[0].IsActive.Should().BeTrue();
        accounts[0].IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task CreateBatteryAssetCommandHandler_ActiveSyncedCustomer_PersistsAsset()
    {
        await using var dbContext = CreateDbContext();
        var customerId = Guid.NewGuid();
        var batteryTypeId = Guid.NewGuid();
        dbContext.CustomerAccounts.Add(CreateCustomer(customerId));
        dbContext.BatteryTypes.Add(CreateBatteryType(batteryTypeId));
        await dbContext.SaveChangesAsync();

        var handler = new CreateBatteryAssetCommandHandler(new UnitOfWork(dbContext), NoOpIntegrationOutbox.Instance, Mock.Of<MediatR.IPublisher>(), MaintenanceOptions);

        var result = await handler.Handle(new CreateBatteryAssetCommand
        {
            SerialNumber = " bat-2026-100 ",
            BatteryTypeId = batteryTypeId,
            CustomerId = customerId,
            InstallDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc)
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(201);
        result.Data!.SerialNumber.Should().Be("BAT-2026-100");

        var asset = await dbContext.BatteryAssets.SingleAsync();
        asset.CustomerId.Should().Be(customerId);
        asset.BatteryTypeId.Should().Be(batteryTypeId);
    }

    [Fact]
    public async Task CreateBatteryAssetCommandHandler_InactiveCustomer_ReturnsNotFound()
    {
        await using var dbContext = CreateDbContext();
        var customerId = Guid.NewGuid();
        var batteryTypeId = Guid.NewGuid();
        dbContext.CustomerAccounts.Add(CreateCustomer(customerId, isActive: false));
        dbContext.BatteryTypes.Add(CreateBatteryType(batteryTypeId));
        await dbContext.SaveChangesAsync();

        var handler = new CreateBatteryAssetCommandHandler(new UnitOfWork(dbContext), NoOpIntegrationOutbox.Instance, Mock.Of<MediatR.IPublisher>(), MaintenanceOptions);

        var result = await handler.Handle(new CreateBatteryAssetCommand
        {
            SerialNumber = "BAT-2026-101",
            BatteryTypeId = batteryTypeId,
            CustomerId = customerId,
            InstallDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc)
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        (await dbContext.BatteryAssets.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task BatchIngestSensorReadingsCommandHandler_PersistsReadingsAndUpdatesAssetLatestTime()
    {
        await using var dbContext = CreateDbContext();
        var customerId = Guid.NewGuid();
        var batteryTypeId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        dbContext.CustomerAccounts.Add(CreateCustomer(customerId));
        dbContext.BatteryTypes.Add(CreateBatteryType(batteryTypeId));
        dbContext.BatteryAssets.Add(CreateBatteryAsset(assetId, batteryTypeId, customerId));
        await dbContext.SaveChangesAsync();

        var handler = new BatchIngestSensorReadingsCommandHandler(
            new UnitOfWork(dbContext),
            new BatteryService.UnitTests.Helpers.NoopIotMetricsRecorder(),
            new BatteryService.UnitTests.Helpers.NoopIotCalibrationCache(),
            new BatteryService.UnitTests.Helpers.NoopTelemetryPublisher(),
            new BatteryService.UnitTests.Helpers.NoopTelemetryStatsService(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<BatchIngestSensorReadingsCommandHandler>.Instance);
        var firstReadingAt = new DateTime(2026, 1, 15, 1, 0, 0, DateTimeKind.Utc);
        var latestReadingAt = new DateTime(2026, 1, 15, 1, 5, 0, DateTimeKind.Utc);

        var result = await handler.Handle(new BatchIngestSensorReadingsCommand
        {
            Items =
            [
                new SensorReadingItem
                {
                    BatteryAssetId = assetId,
                    Time = firstReadingAt,
                    Voltage = 12.8m,
                    Current = 4.2m,
                    Temperature = 31.5m,
                    SocPercent = 75,
                    CycleCount = 120,
                    SourceDeviceId = " rack-a-01 "
                },
                new SensorReadingItem
                {
                    BatteryAssetId = assetId,
                    Time = latestReadingAt,
                    Voltage = 12.9m,
                    Current = 4.3m,
                    Temperature = 31.7m,
                    SocPercent = 76,
                    CycleCount = 121,
                    SourceDeviceId = "rack-a-01"
                }
            ]
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Inserted.Should().Be(2);
        (await dbContext.SensorReadings.CountAsync()).Should().Be(2);

        var asset = await dbContext.BatteryAssets.SingleAsync();
        asset.LastSensorReadingAt.Should().Be(latestReadingAt);
    }

    [Fact]
    public async Task AccountDeletedConsumer_MarksAccountInactiveAndDeleted()
    {
        await using var dbContext = CreateDbContext();
        var customerId = Guid.NewGuid();
        dbContext.CustomerAccounts.Add(CreateCustomer(customerId));
        await dbContext.SaveChangesAsync();

        var consumer = new AccountDeletedConsumer(new UnitOfWork(dbContext), new InMemoryInboxStore());
        var evt = new AccountDeletedEvent(customerId, "c@x.com", "test");
        var ctx = new Mock<ConsumeContext<AccountDeletedEvent>>();
        ctx.SetupGet(c => c.Message).Returns(evt);
        ctx.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

        await consumer.Consume(ctx.Object);

        var account = await dbContext.CustomerAccounts.IgnoreQueryFilters().SingleAsync();
        account.IsActive.Should().BeFalse();
        account.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task AccountDeletedConsumer_AccountNotFound_NoOp()
    {
        await using var dbContext = CreateDbContext();
        var consumer = new AccountDeletedConsumer(new UnitOfWork(dbContext), new InMemoryInboxStore());
        var evt = new AccountDeletedEvent(Guid.NewGuid(), "c@x.com", "test");
        var ctx = new Mock<ConsumeContext<AccountDeletedEvent>>();
        ctx.SetupGet(c => c.Message).Returns(evt);
        ctx.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

        await consumer.Consume(ctx.Object);
        (await dbContext.CustomerAccounts.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AccountStatusChangedConsumer_InactivatesAccount()
    {
        await using var dbContext = CreateDbContext();
        var customerId = Guid.NewGuid();
        dbContext.CustomerAccounts.Add(CreateCustomer(customerId));
        await dbContext.SaveChangesAsync();

        var consumer = new AccountStatusChangedConsumer(new UnitOfWork(dbContext), new InMemoryInboxStore());
        var evt = new AccountStatusChangedEvent(customerId, "new@x.com", 1, 2, "manual");
        var ctx = new Mock<ConsumeContext<AccountStatusChangedEvent>>();
        ctx.SetupGet(c => c.Message).Returns(evt);
        ctx.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

        await consumer.Consume(ctx.Object);

        var account = await dbContext.CustomerAccounts.SingleAsync();
        account.IsActive.Should().BeFalse();
        account.Email.Should().Be("new@x.com");
    }

    [Fact]
    public async Task AccountStatusChangedConsumer_ReactivatesWhenStatusActive()
    {
        await using var dbContext = CreateDbContext();
        var customerId = Guid.NewGuid();
        var existing = CreateCustomer(customerId, isActive: false);
        dbContext.CustomerAccounts.Add(existing);
        await dbContext.SaveChangesAsync();

        var consumer = new AccountStatusChangedConsumer(new UnitOfWork(dbContext), new InMemoryInboxStore());
        var evt = new AccountStatusChangedEvent(customerId, "C@X.com", 2, 1, null);
        var ctx = new Mock<ConsumeContext<AccountStatusChangedEvent>>();
        ctx.SetupGet(c => c.Message).Returns(evt);
        ctx.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

        await consumer.Consume(ctx.Object);

        var account = await dbContext.CustomerAccounts.SingleAsync();
        account.IsActive.Should().BeTrue();
        account.Email.Should().Be("c@x.com");
    }

    [Fact]
    public async Task AccountStatusChangedConsumer_AccountNotFound_NoOp()
    {
        await using var dbContext = CreateDbContext();
        var consumer = new AccountStatusChangedConsumer(new UnitOfWork(dbContext), new InMemoryInboxStore());
        var evt = new AccountStatusChangedEvent(Guid.NewGuid(), "x@x.com", 1, 2, null);
        var ctx = new Mock<ConsumeContext<AccountStatusChangedEvent>>();
        ctx.SetupGet(c => c.Message).Returns(evt);
        ctx.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

        await consumer.Consume(ctx.Object);
        (await dbContext.CustomerAccounts.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AccountActivatedConsumer_NonCustomerRole_Skipped()
    {
        await using var dbContext = CreateDbContext();
        var consumer = new BatteryAccountActivatedConsumer(new UnitOfWork(dbContext), new InMemoryInboxStore());
        var evt = new AccountActivatedEvent(Guid.NewGuid(), "x@x", "n", null, "Admin", "test");
        var ctx = new Mock<ConsumeContext<AccountActivatedEvent>>();
        ctx.SetupGet(c => c.Message).Returns(evt);
        ctx.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

        await consumer.Consume(ctx.Object);
        (await dbContext.CustomerAccounts.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AccountActivatedConsumer_ExistingAccount_UpdatesFields()
    {
        await using var dbContext = CreateDbContext();
        var customerId = Guid.NewGuid();
        dbContext.CustomerAccounts.Add(CreateCustomer(customerId, isActive: false));
        await dbContext.SaveChangesAsync();

        var consumer = new BatteryAccountActivatedConsumer(new UnitOfWork(dbContext), new InMemoryInboxStore());
        var evt = new AccountActivatedEvent(customerId, "NEW@X.COM", " Name ", " 0901 ", "Customer", "test");
        var ctx = new Mock<ConsumeContext<AccountActivatedEvent>>();
        ctx.SetupGet(c => c.Message).Returns(evt);
        ctx.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

        await consumer.Consume(ctx.Object);

        var account = await dbContext.CustomerAccounts.SingleAsync();
        account.IsActive.Should().BeTrue();
        account.Email.Should().Be("new@x.com");
        account.FullName.Should().Be("Name");
        account.PhoneNumber.Should().Be("0901");
        account.Role.Should().Be("Customer");
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"battery-service-integration-{Guid.NewGuid()}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var currentUser = new CurrentUserService(new HttpContextAccessor());
        var interceptor = new AuditableEntityInterceptor(currentUser);

        return new ApplicationDbContext(options, interceptor);
    }

    private static CustomerAccount CreateCustomer(Guid id, bool isActive = true)
    {
        return new CustomerAccount
        {
            Id = id,
            Email = $"{id:N}@example.com",
            FullName = "Integration Customer",
            Role = "Customer",
            IsActive = isActive,
            LastSyncedAtUtc = DateTime.UtcNow
        };
    }

    private static BatteryType CreateBatteryType(Guid id)
    {
        return new BatteryType
        {
            Id = id,
            Name = $"LiFePO4 {id:N}",
            NominalCapacityAh = 100,
            NominalVoltage = 12,
            Chemistry = BatteryChemistryEnum.LiFePO4,
            MaxCycleCount = 3000
        };
    }

    private static BatteryAsset CreateBatteryAsset(Guid id, Guid batteryTypeId, Guid customerId)
    {
        return new BatteryAsset
        {
            Id = id,
            SerialNumber = $"BAT-{id:N}",
            BatteryTypeId = batteryTypeId,
            CustomerId = customerId,
            InstallDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            WarrantyStatus = WarrantyStatusEnum.Active,
            Status = BatteryStatusEnum.Active
        };
    }

    /// <summary>
    /// GH-764 — bám đúng vòng đời ba bước: giữ chỗ → chốt khi xong → nhả khi lỗi.
    /// Bản giả mà chốt ngay từ lúc xin chỗ sẽ che mất chính lỗi mà issue nói tới.
    /// </summary>
    private sealed class InMemoryInboxStore : IInboxStore
    {
        private readonly ConcurrentDictionary<string, (bool Completed, string Token)> _entries = new();

        private static string Key(Guid messageId, string consumerName) => $"{consumerName}:{messageId}";

        public Task<InboxClaim> TryBeginAsync(
            Guid messageId,
            string consumerName,
            CancellationToken cancellationToken = default)
        {
            var token = Guid.NewGuid().ToString("N");
            if (_entries.TryAdd(Key(messageId, consumerName), (false, token)))
                return Task.FromResult(new InboxClaim(InboxClaimStatus.Claimed, token));

            return Task.FromResult(_entries[Key(messageId, consumerName)].Completed
                ? InboxClaim.Completed
                : InboxClaim.Busy);
        }

        public Task CompleteAsync(Guid messageId, string consumerName, string token, CancellationToken cancellationToken = default)
        {
            var key = Key(messageId, consumerName);
            if (_entries.TryGetValue(key, out var e) && e.Token == token)
                _entries[key] = (true, token);
            return Task.CompletedTask;
        }

        public Task ReleaseAsync(Guid messageId, string consumerName, string token, CancellationToken cancellationToken = default)
        {
            var key = Key(messageId, consumerName);
            if (_entries.TryGetValue(key, out var e) && e.Token == token && !e.Completed)
                _entries.TryRemove(key, out _);
            return Task.CompletedTask;
        }
    }
}
