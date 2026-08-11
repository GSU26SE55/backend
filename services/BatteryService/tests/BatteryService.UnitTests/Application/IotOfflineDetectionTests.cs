using BatteryService.Application.Services;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.Infrastructure.Implements.Repositories;
using BatteryService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SharedContracts.Events;
using SharedContracts.Events.Root;
using SharedContracts.Interfaces;
using SharedInfrastructure.Persistence.Interceptors;
using SharedInfrastructure.Services;

namespace BatteryService.UnitTests.Application;

/// <summary>
/// Uses SQLite because the production transition intentionally relies on relational
/// ExecuteUpdate for an atomic Active/LastSeen claim; an IQueryable mock cannot validate that.
/// </summary>
public sealed class IotOfflineDetectionTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public IotOfflineDetectionTests() => _connection.Open();

    public void Dispose() => _connection.Dispose();

    private sealed class CapturingOutbox : IIntegrationEventOutboxWriter
    {
        public readonly List<IntegrationEvent> Events = [];

        public Task WriteAsync<TEvent>(
            TEvent @event,
            CancellationToken cancellationToken = default)
            where TEvent : IntegrationEvent
        {
            Events.Add(@event);
            return Task.CompletedTask;
        }
    }

    private ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;
        var interceptor = new AuditableEntityInterceptor(
            new CurrentUserService(new HttpContextAccessor()));
        var db = new ApplicationDbContext(options, interceptor);
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task Detect_MarksStaleDeviceOffline_CreatesOneIncident_AndPublishesOnce()
    {
        await using var db = CreateDb();
        var siteId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var typeId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();

        db.Sites.Add(new Site
        {
            Id = siteId,
            Name = "Site B",
            CustomerId = customerId,
            InstallDate = DateTime.UtcNow.AddYears(-1)
        });
        db.BatteryTypes.Add(new BatteryType
        {
            Id = typeId,
            Name = "LiFePO4",
            Manufacturer = "Test",
            NominalVoltage = 48,
            NominalCapacityAh = 100
        });
        db.BatteryAssets.Add(new BatteryAsset
        {
            Id = assetId,
            SerialNumber = "BAT-Z",
            SiteId = siteId,
            CustomerId = customerId,
            BatteryTypeId = typeId,
            InstallDate = DateTime.UtcNow.AddYears(-1)
        });
        db.IotDevices.Add(new IotDevice
        {
            Id = deviceId,
            DeviceCode = "ESP-OFF",
            DisplayName = "test",
            SiteId = siteId,
            Status = IotDeviceStatusEnum.Active,
            ApiKeyHash = "h",
            ApiKeyLastFour = "abcd",
            ApiKeyScopes = IotApiKeyScopeEnum.EdgeDeviceDefault,
            LastSeenAt = DateTime.UtcNow.AddMinutes(-10),
            HeartbeatIntervalSeconds = 60
        });
        db.IotDeviceCalibrations.Add(new IotDeviceCalibration
        {
            Id = Guid.NewGuid(),
            IotDeviceId = deviceId,
            BatteryAssetId = assetId,
            Channel = "primary"
        });
        await db.SaveChangesAsync();

        var outbox = new CapturingOutbox();
        var service = new IotDeviceOfflineDetectionService(
            new UnitOfWork(db),
            outbox,
            new Helpers.NoopIotMetricsRecorder(),
            NullLogger<IotDeviceOfflineDetectionService>.Instance);

        var first = await service.DetectAsync(300, 10, default);
        var second = await service.DetectAsync(300, 10, default);

        first.Scanned.Should().Be(1);
        first.MarkedOffline.Should().Be(1);
        second.MarkedOffline.Should().Be(0);
        db.ChangeTracker.Clear();
        (await db.IotDevices.SingleAsync()).Status.Should().Be(IotDeviceStatusEnum.Offline);
        var alert = await db.Alerts.SingleAsync();
        alert.IotDeviceId.Should().Be(deviceId);
        alert.BatteryAssetId.Should().BeNull("offline is one device-level incident, not one alert per battery");

        outbox.Events.Should().ContainSingle(e => e is IotDeviceWentOfflineEvent);
        var evt = (IotDeviceWentOfflineEvent)outbox.Events.Single();
        evt.AffectedBatteryCount.Should().Be(1);
        evt.AlertId.Should().Be(alert.Id);
        evt.CustomerId.Should().Be(customerId);
    }

    [Fact]
    public async Task Detect_UsesHeartbeatCadenceAndSkipsFreshDevice()
    {
        await using var db = CreateDb();
        var siteId = Guid.NewGuid();
        db.Sites.Add(new Site
        {
            Id = siteId,
            Name = "Fresh Site",
            CustomerId = Guid.NewGuid(),
            InstallDate = DateTime.UtcNow
        });
        db.IotDevices.Add(new IotDevice
        {
            Id = Guid.NewGuid(),
            DeviceCode = "ESP-FRESH",
            DisplayName = "test",
            SiteId = siteId,
            Status = IotDeviceStatusEnum.Active,
            ApiKeyHash = "h",
            ApiKeyLastFour = "abcd",
            LastSeenAt = DateTime.UtcNow.AddMinutes(-6),
            HeartbeatIntervalSeconds = 600
        });
        await db.SaveChangesAsync();

        var outbox = new CapturingOutbox();
        var service = new IotDeviceOfflineDetectionService(
            new UnitOfWork(db),
            outbox,
            new Helpers.NoopIotMetricsRecorder(),
            NullLogger<IotDeviceOfflineDetectionService>.Instance);

        var result = await service.DetectAsync(300, 10, default);

        result.MarkedOffline.Should().Be(0);
        outbox.Events.Should().BeEmpty();
        (await db.IotDevices.SingleAsync()).Status.Should().Be(IotDeviceStatusEnum.Active);
    }
}
