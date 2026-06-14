using BatteryService.Application.CQRS.Command.SensorReading;
using BatteryService.Application.CQRS.Handler.SensorReading;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.UnitTests.Helpers;

namespace BatteryService.UnitTests.Application;

/// <summary>
/// Sprint IoT-1 #246/#247 — verify handler honors header-driven fields
/// (đã được controller copy từ X-Device-Code/Idempotency-Key/claim sang command).
/// Field có [JsonIgnore][BindNever] → client không thể override qua body.
/// Idempotency-Key chỉ được capture trong scope IoT-1; dedup thực sự defer Sprint IoT-2.
/// </summary>
public class IngestHeaderCaptureTests
{
    private static IotDevice MakeDevice(Guid id) => new()
    {
        Id = id,
        DeviceCode = "ESP-HDR",
        DisplayName = "test",
        SiteId = Guid.NewGuid(),
        Status = IotDeviceStatusEnum.Active,
        ApiKeyHash = "h",
        ApiKeyLastFour = "abcd",
        ApiKeyScopes = IotApiKeyScopeEnum.EdgeDeviceDefault,
        LastSeenAt = DateTime.UtcNow.AddMinutes(-1),
        HeartbeatIntervalSeconds = 60
    };

    [Fact]
    public async Task BatchIngest_UpdatesDevice_LastSeenAt_WhenAuthenticated()
    {
        var deviceId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var device = MakeDevice(deviceId);
        // Sprint IoT-2 #IoT2-18 — asset.SiteId phải match device.SiteId, không sẽ bị 403.
        var asset = new BatteryAsset { Id = assetId, SerialNumber = "BAT-HDR", SiteId = device.SiteId };

        var uow = new MockUnitOfWorkBuilder()
            .WithIotDevices(device)
            .WithBatteryAssets(asset);
        var handler = new BatchIngestSensorReadingsCommandHandler(uow.Build(), new BatteryService.UnitTests.Helpers.NoopIotMetricsRecorder(), new BatteryService.UnitTests.Helpers.NoopIotCalibrationCache(), Microsoft.Extensions.Logging.Abstractions.NullLogger<BatchIngestSensorReadingsCommandHandler>.Instance);

        var before = device.LastSeenAt;
        var result = await handler.Handle(new BatchIngestSensorReadingsCommand
        {
            AuthenticatedDeviceId = deviceId,
            DeviceCode = "ESP-HDR",
            IdempotencyKey = "test-key-1",
            Items = new()
            {
                new() { BatteryAssetId = assetId, Time = DateTime.UtcNow, Voltage = 3.7m, Current = 1m, Temperature = 25m, SocPercent = 50m }
            }
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Inserted.Should().Be(1);
        device.LastSeenAt.Should().BeAfter(before ?? DateTime.MinValue,
            "Handler phải update LastSeenAt khi có AuthenticatedDeviceId (Sprint IoT-1 §247).");
    }

    [Fact]
    public async Task BatchIngest_FlipsStatus_FromOfflineToActive()
    {
        var deviceId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var device = MakeDevice(deviceId);
        device.Status = IotDeviceStatusEnum.Offline;
        // Sprint IoT-2 #IoT2-18 — asset.SiteId phải match device.SiteId.
        var asset = new BatteryAsset { Id = assetId, SerialNumber = "BAT-OFF", SiteId = device.SiteId };

        var uow = new MockUnitOfWorkBuilder()
            .WithIotDevices(device)
            .WithBatteryAssets(asset);
        var handler = new BatchIngestSensorReadingsCommandHandler(uow.Build(), new BatteryService.UnitTests.Helpers.NoopIotMetricsRecorder(), new BatteryService.UnitTests.Helpers.NoopIotCalibrationCache(), Microsoft.Extensions.Logging.Abstractions.NullLogger<BatchIngestSensorReadingsCommandHandler>.Instance);

        await handler.Handle(new BatchIngestSensorReadingsCommand
        {
            AuthenticatedDeviceId = deviceId,
            Items = new()
            {
                new() { BatteryAssetId = assetId, Time = DateTime.UtcNow, Voltage = 3.7m, Current = 1m, Temperature = 25m, SocPercent = 50m }
            }
        }, default);

        device.Status.Should().Be(IotDeviceStatusEnum.Active,
            "Reading mới phải flip Offline/Pending → Active (auto-recover khi reconnect).");
    }

    [Fact]
    public async Task BatchIngest_ResolvesSerial_ToAssetId()
    {
        var assetId = Guid.NewGuid();
        var asset = new BatteryAsset { Id = assetId, SerialNumber = "BAT-RESOLVE", IsDeleted = false };

        var uow = new MockUnitOfWorkBuilder().WithBatteryAssets(asset);
        var handler = new BatchIngestSensorReadingsCommandHandler(uow.Build(), new BatteryService.UnitTests.Helpers.NoopIotMetricsRecorder(), new BatteryService.UnitTests.Helpers.NoopIotCalibrationCache(), Microsoft.Extensions.Logging.Abstractions.NullLogger<BatchIngestSensorReadingsCommandHandler>.Instance);

        // Item chỉ có BatteryAssetSerial — handler phải resolve về Id.
        var result = await handler.Handle(new BatchIngestSensorReadingsCommand
        {
            Items = new()
            {
                new()
                {
                    BatteryAssetId = Guid.Empty,
                    BatteryAssetSerial = "BAT-RESOLVE",
                    Time = DateTime.UtcNow,
                    Voltage = 3.7m, Current = 1m, Temperature = 25m, SocPercent = 50m
                }
            }
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Inserted.Should().Be(1);
        result.Data.Skipped.Should().Be(0);
    }

    [Fact]
    public async Task BatchIngest_SkipsItem_WhenSerialNotFound()
    {
        var uow = new MockUnitOfWorkBuilder();
        var handler = new BatchIngestSensorReadingsCommandHandler(uow.Build(), new BatteryService.UnitTests.Helpers.NoopIotMetricsRecorder(), new BatteryService.UnitTests.Helpers.NoopIotCalibrationCache(), Microsoft.Extensions.Logging.Abstractions.NullLogger<BatchIngestSensorReadingsCommandHandler>.Instance);

        var result = await handler.Handle(new BatchIngestSensorReadingsCommand
        {
            Items = new()
            {
                new()
                {
                    BatteryAssetId = Guid.Empty,
                    BatteryAssetSerial = "BAT-MISSING",
                    Time = DateTime.UtcNow,
                    Voltage = 3.7m, Current = 1m, Temperature = 25m, SocPercent = 50m
                }
            }
        }, default);

        // Item bị skip (không throw) → gateway biết qua Skipped count.
        result.IsSuccess.Should().BeTrue();
        result.Data!.Inserted.Should().Be(0);
        result.Data.Skipped.Should().Be(1);
    }

    [Fact]
    public async Task BatchIngest_HonorsSourceTypePerItem()
    {
        var assetId = Guid.NewGuid();
        var asset = new BatteryAsset { Id = assetId, SerialNumber = "BAT-SRC" };

        var uow = new MockUnitOfWorkBuilder().WithBatteryAssets(asset);
        var captured = new List<SensorReading>();
        uow.SensorReadings.Setup(r => r.AddAsync(It.IsAny<SensorReading>()))
           .Callback<SensorReading>(captured.Add)
           .Returns(Task.CompletedTask);

        var handler = new BatchIngestSensorReadingsCommandHandler(uow.Build(), new BatteryService.UnitTests.Helpers.NoopIotMetricsRecorder(), new BatteryService.UnitTests.Helpers.NoopIotCalibrationCache(), Microsoft.Extensions.Logging.Abstractions.NullLogger<BatchIngestSensorReadingsCommandHandler>.Instance);

        // B9 (#154) — ingest accept sourceType per item (BMS/IotGateway/External).
        await handler.Handle(new BatchIngestSensorReadingsCommand
        {
            Items = new()
            {
                new() { BatteryAssetId = assetId, Time = DateTime.UtcNow, Voltage = 3.7m, Current = 1m, Temperature = 25m, SocPercent = 50m,
                        SourceType = SensorReadingSourceTypeEnum.Bms },
                new() { BatteryAssetId = assetId, Time = DateTime.UtcNow.AddSeconds(1), Voltage = 3.7m, Current = 1m, Temperature = 25m, SocPercent = 50m,
                        SourceType = SensorReadingSourceTypeEnum.External }
            }
        }, default);

        captured.Should().HaveCount(2);
        captured[0].SourceType.Should().Be(SensorReadingSourceTypeEnum.Bms);
        captured[1].SourceType.Should().Be(SensorReadingSourceTypeEnum.External);
    }
}
