using BatteryService.Application.Services;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.UnitTests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using SharedContracts.Events;
using SharedContracts.Events.Root;
using SharedContracts.Interfaces;

namespace BatteryService.UnitTests.Application;

public class IotOfflineDetectionTests
{
    private sealed class CapturingOutbox : IIntegrationEventOutboxWriter
    {
        public readonly List<IntegrationEvent> Events = new();
        public Task WriteAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : IntegrationEvent
        {
            Events.Add(@event);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Detect_MarksStaleDeviceOffline_AndPublishesEvent()
    {
        var siteId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var staleDevice = new IotDevice
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
            HeartbeatIntervalSeconds = 60,
            Site = new Site { Id = siteId, Name = "Site B", CustomerId = Guid.NewGuid() }
        };

        var uow = new MockUnitOfWorkBuilder()
            .WithIotDevices(staleDevice)
            .WithBatteryAssets(new BatteryAsset { Id = assetId, SerialNumber = "BAT-Z", SiteId = siteId });

        var outbox = new CapturingOutbox();
        var svc = new IotDeviceOfflineDetectionService(uow.Build(), outbox, NullLogger<IotDeviceOfflineDetectionService>.Instance);

        var result = await svc.DetectAsync(offlineAfterSeconds: 300, batchSize: 10, default);

        result.Scanned.Should().Be(1);
        result.MarkedOffline.Should().Be(1);
        staleDevice.Status.Should().Be(IotDeviceStatusEnum.Offline);
        outbox.Events.Should().ContainSingle(e => e is IotDeviceWentOfflineEvent);
        var evt = (IotDeviceWentOfflineEvent)outbox.Events.Single();
        evt.DeviceCode.Should().Be("ESP-OFF");
        evt.AffectedBatteryCount.Should().Be(1);
        evt.AlertId.Should().NotBeNull();
    }

    [Fact]
    public async Task Detect_SkipsDeviceNotYetStale()
    {
        var deviceId = Guid.NewGuid();
        var freshDevice = new IotDevice
        {
            Id = deviceId,
            DeviceCode = "ESP-FRESH",
            DisplayName = "test",
            SiteId = Guid.NewGuid(),
            Status = IotDeviceStatusEnum.Active,
            ApiKeyHash = "h",
            ApiKeyLastFour = "abcd",
            LastSeenAt = DateTime.UtcNow.AddSeconds(-30)
        };

        var uow = new MockUnitOfWorkBuilder().WithIotDevices(freshDevice);
        var outbox = new CapturingOutbox();
        var svc = new IotDeviceOfflineDetectionService(uow.Build(), outbox, NullLogger<IotDeviceOfflineDetectionService>.Instance);

        var result = await svc.DetectAsync(offlineAfterSeconds: 300, batchSize: 10, default);
        result.MarkedOffline.Should().Be(0);
        outbox.Events.Should().BeEmpty();
        freshDevice.Status.Should().Be(IotDeviceStatusEnum.Active);
    }
}
