using BatteryService.Application.CQRS.Command.IotDevice;
using BatteryService.Application.CQRS.Command.SensorReading;
using BatteryService.Application.CQRS.Handler.IotDevice;
using BatteryService.Application.CQRS.Handler.SensorReading;
using BatteryService.Application.CQRS.Query.IotDevice;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.Infrastructure.Implements.Services;
using BatteryService.UnitTests.Helpers;

namespace BatteryService.UnitTests.Application;

public class IotDeviceLifecycleHandlerTests
{
    private static IotDevice ActiveDevice(Guid id, Guid siteId) => new()
    {
        Id = id,
        DeviceCode = "ESP32-LF",
        DisplayName = "test",
        SiteId = siteId,
        Status = IotDeviceStatusEnum.Pending,
        ApiKeyHash = "hash",
        ApiKeyLastFour = "abcd",
        ApiKeyScopes = IotApiKeyScopeEnum.EdgeDeviceDefault,
        ApiKeyIssuedAt = DateTime.UtcNow.AddDays(-1),
        HeartbeatIntervalSeconds = 60
    };

    [Fact]
    public async Task Provision_FlipsStatusActive_AndStoresFirmwareVersion()
    {
        var deviceId = Guid.NewGuid();
        var uow = new MockUnitOfWorkBuilder()
            .WithIotDevices(ActiveDevice(deviceId, Guid.NewGuid()));
        var handler = new ProvisionIotDeviceCommandHandler(uow.Build(), TestMqttBrokerEndpointProvider.Enabled(), new IotApiKeyService(uow.Build()), NoopMqttPasswordFileSync.Instance());

        var result = await handler.Handle(new ProvisionIotDeviceCommand
        {
            DeviceId = deviceId,
            DeviceCode = "ESP32-LF",
            FirmwareVersion = "1.0.0",
            HardwareRevision = "v1.0",
            DeviceTimestamp = DateTime.UtcNow
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data!.HeartbeatIntervalSeconds.Should().Be(60);
    }

    [Fact]
    public async Task Provision_MapsSiteBatteriesToStableModbusUnitIds()
    {
        var deviceId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var otherSiteId = Guid.NewGuid();
        var uow = new MockUnitOfWorkBuilder()
            .WithIotDevices(ActiveDevice(deviceId, siteId))
            .WithBatteryAssets(
                new BatteryAsset { Id = Guid.NewGuid(), SerialNumber = "BAT-002", SiteId = siteId },
                new BatteryAsset { Id = Guid.NewGuid(), SerialNumber = "BAT-001", SiteId = siteId },
                new BatteryAsset { Id = Guid.NewGuid(), SerialNumber = "BAT-OTHER", SiteId = otherSiteId },
                new BatteryAsset { Id = Guid.NewGuid(), SerialNumber = "BAT-DELETED", SiteId = siteId, IsDeleted = true });
        var handler = new ProvisionIotDeviceCommandHandler(
            uow.Build(), TestMqttBrokerEndpointProvider.Enabled(),
            new IotApiKeyService(uow.Build()), NoopMqttPasswordFileSync.Instance());

        var result = await handler.Handle(new ProvisionIotDeviceCommand
        {
            DeviceId = deviceId,
            DeviceCode = "ESP32-LF",
            FirmwareVersion = "1.0.0",
            DeviceTimestamp = DateTime.UtcNow
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data!.BatteryMappings.Should().BeEquivalentTo(
            new[]
            {
                new { BatteryAssetSerial = "BAT-001", UnitId = (int?)1, SensorSourceCode = "primary" },
                new { BatteryAssetSerial = "BAT-002", UnitId = (int?)2, SensorSourceCode = "primary" }
            }, options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task Provision_Fails_WhenClockSkewExceedsThreshold()
    {
        var deviceId = Guid.NewGuid();
        var uow = new MockUnitOfWorkBuilder()
            .WithIotDevices(ActiveDevice(deviceId, Guid.NewGuid()));
        var handler = new ProvisionIotDeviceCommandHandler(uow.Build(), TestMqttBrokerEndpointProvider.Enabled(), new IotApiKeyService(uow.Build()), NoopMqttPasswordFileSync.Instance());

        var result = await handler.Handle(new ProvisionIotDeviceCommand
        {
            DeviceId = deviceId,
            DeviceCode = "ESP32-LF",
            FirmwareVersion = "1.0.0",
            DeviceTimestamp = DateTime.UtcNow.AddMinutes(10) // > 5 phút
        }, default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(422);
    }

    [Fact]
    public async Task RotateKey_ReplacesStoredPlaintext_AndGetByIdReturnsNewKey()
    {
        var deviceId = Guid.NewGuid();
        var device = ActiveDevice(deviceId, Guid.NewGuid());
        device.ApiKeyPlaintext = "iotk_old-key-abcd"; // key cũ đã lưu
        var uow = new MockUnitOfWorkBuilder().WithIotDevices(device);
        var rotateHandler = new RotateIotDeviceApiKeyCommandHandler(uow.Build(), new IotApiKeyService(uow.Build()), TestMqttBrokerEndpointProvider.Enabled(), NoopMqttPasswordFileSync.Instance());

        var rotated = await rotateHandler.Handle(new RotateIotDeviceApiKeyCommand { Id = deviceId }, default);

        rotated.IsSuccess.Should().BeTrue();
        var newRawKey = rotated.Data!.RawApiKey;
        newRawKey.Should().StartWith("iotk_").And.NotBe("iotk_old-key-abcd");

        // GET by id phải trả key MỚI (đã replace), không phải key cũ.
        var getById = new GetIotDeviceByIdQueryHandler(uow.Build(), TestMqttBrokerEndpointProvider.Enabled());
        var detail = await getById.Handle(new GetIotDeviceByIdQuery { Id = deviceId }, default);

        detail.Data!.ApiKey.Should().Be(newRawKey);
        detail.Data.ApiKey.Should().NotBe("iotk_old-key-abcd");
    }

    [Fact]
    public async Task Heartbeat_InsertsHistoryRowAndUpdatesLastSeen()
    {
        var deviceId = Guid.NewGuid();
        var device = ActiveDevice(deviceId, Guid.NewGuid());
        device.Status = IotDeviceStatusEnum.Offline; // sẽ flip lên Active
        var uow = new MockUnitOfWorkBuilder().WithIotDevices(device);
        var handler = new IotDeviceHeartbeatCommandHandler(uow.Build(), new BatteryService.UnitTests.Helpers.NoopIotMetricsRecorder());

        var result = await handler.Handle(new IotDeviceHeartbeatCommand
        {
            DeviceId = deviceId,
            DeviceCode = "ESP32-LF",
            FirmwareVersion = "1.0.0",
            DeviceTimestamp = DateTime.UtcNow,
            RssiDbm = -55,
            FreeMemoryPercent = 65m,
            UptimeSeconds = 3600,
            QueuedReadingCount = 0
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data!.NextHeartbeatInSeconds.Should().Be(60);
        result.Data.ClockSkewWarning.Should().BeFalse();
        uow.IotDeviceHeartbeats.Verify(r => r.AddAsync(It.IsAny<IotDeviceHeartbeat>()), Times.Once);
    }

    [Fact]
    public async Task BatchIngest_RejectsOutlierVoltage()
    {
        var assetId = Guid.NewGuid();
        var uow = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(new BatteryAsset { Id = assetId, SerialNumber = "BAT-1", IsDeleted = false });
        var handler = new BatchIngestSensorReadingsCommandHandler(uow.Build(), new BatteryService.UnitTests.Helpers.NoopIotMetricsRecorder(), new BatteryService.UnitTests.Helpers.NoopIotCalibrationCache(), new BatteryService.UnitTests.Helpers.NoopTelemetryPublisher(), new BatteryService.UnitTests.Helpers.NoopTelemetryStatsService(), Microsoft.Extensions.Logging.Abstractions.NullLogger<BatchIngestSensorReadingsCommandHandler>.Instance);

        var result = await handler.Handle(new BatchIngestSensorReadingsCommand
        {
            Items = new List<SensorReadingItem>
            {
                // Sprint IoT-2 #IoT2-17 — MaxVoltage=1000V; 1500V > ngưỡng → outlier reject.
                new() { BatteryAssetId = assetId, Time = DateTime.UtcNow, Voltage = 1500m, Current = 1m, Temperature = 25m, SocPercent = 50m },
                new() { BatteryAssetId = assetId, Time = DateTime.UtcNow, Voltage = 3.7m, Current = 1m, Temperature = 25m, SocPercent = 50m }
            }
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Inserted.Should().Be(1);
        result.Data.Skipped.Should().Be(1);
    }

    [Fact]
    public async Task BatchIngest_AppliesCalibrationScaleAndOffset()
    {
        var assetId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        // Sprint IoT-2 #IoT2-18 — device.SiteId phải khớp asset.SiteId, không sẽ bị reject 403.
        var siteId = Guid.NewGuid();
        var uow = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(new BatteryAsset { Id = assetId, SerialNumber = "BAT-2", SiteId = siteId })
            .WithIotDevices(new IotDevice { Id = deviceId, DeviceCode = "ESP32-X", DisplayName = "x", SiteId = siteId, ApiKeyHash = "h", ApiKeyLastFour = "abcd", ApiKeyScopes = IotApiKeyScopeEnum.EdgeDeviceDefault })
            .WithIotDeviceCalibrations(new IotDeviceCalibration
            {
                Id = Guid.NewGuid(),
                IotDeviceId = deviceId,
                Channel = "voltage",
                Scale = 1.1m,
                Offset = 0.5m,
                Unit = "V",
                CalibratedAt = DateTime.UtcNow.AddDays(-1)
            });
        var handler = new BatchIngestSensorReadingsCommandHandler(uow.Build(), new BatteryService.UnitTests.Helpers.NoopIotMetricsRecorder(), new BatteryService.UnitTests.Helpers.NoopIotCalibrationCache(), new BatteryService.UnitTests.Helpers.NoopTelemetryPublisher(), new BatteryService.UnitTests.Helpers.NoopTelemetryStatsService(), Microsoft.Extensions.Logging.Abstractions.NullLogger<BatchIngestSensorReadingsCommandHandler>.Instance);

        var captured = new List<SensorReading>();
        uow.SensorReadings.Setup(r => r.AddAsync(It.IsAny<SensorReading>()))
           .Callback<SensorReading>(captured.Add)
           .Returns(Task.CompletedTask);

        var result = await handler.Handle(new BatchIngestSensorReadingsCommand
        {
            AuthenticatedDeviceId = deviceId,
            Items = new List<SensorReadingItem>
            {
                new() { BatteryAssetId = assetId, Time = DateTime.UtcNow, Voltage = 3m, Current = 1m, Temperature = 25m, SocPercent = 50m }
            }
        }, default);

        result.IsSuccess.Should().BeTrue();
        captured.Should().ContainSingle();
        captured[0].Voltage.Should().Be(3m * 1.1m + 0.5m);
    }
}
