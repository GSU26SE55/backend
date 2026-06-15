using BatteryService.Application.CQRS.Handler.IotDevice;
using BatteryService.Application.CQRS.Query.IotDevice;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.UnitTests.Helpers;

namespace BatteryService.UnitTests.Application;

public class IotFirmwareCheckTests
{
    [Fact]
    public async Task FirmwareCheck_ReturnsNoUpdate_WhenCurrentMatchesTarget()
    {
        var deviceId = Guid.NewGuid();
        var fwId = Guid.NewGuid();
        var fw = new IotFirmwareRelease
        {
            Id = fwId,
            Version = "1.0.0",
            HardwareRevision = "v1",
            ArtifactUrl = "https://x",
            Sha256Checksum = new string('a', 64),
            ArtifactSizeBytes = 1024,
            IsPublished = true
        };
        var device = new IotDevice
        {
            Id = deviceId,
            DeviceCode = "ESP-FW",
            DisplayName = "x",
            SiteId = Guid.NewGuid(),
            ApiKeyHash = "h",
            ApiKeyLastFour = "abcd",
            ApiKeyScopes = IotApiKeyScopeEnum.EdgeDeviceDefault,
            TargetFirmwareReleaseId = fwId,
            TargetFirmwareRelease = fw,
            CurrentFirmwareVersion = "1.0.0"
        };

        var uow = new MockUnitOfWorkBuilder()
            .WithIotDevices(device)
            .WithIotFirmwareReleases(fw);
        var handler = new CheckIotFirmwareUpdateQueryHandler(uow.Build());

        var result = await handler.Handle(new CheckIotFirmwareUpdateQuery { DeviceId = deviceId, CurrentVersion = "1.0.0" }, default);
        result.IsSuccess.Should().BeTrue();
        result.Data!.UpdateAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task FirmwareCheck_ReturnsArtifact_AndCreatesUpdateLog()
    {
        var deviceId = Guid.NewGuid();
        var fwId = Guid.NewGuid();
        var fw = new IotFirmwareRelease
        {
            Id = fwId,
            Version = "1.1.0",
            HardwareRevision = "v1",
            ArtifactUrl = "https://files/fw-1.1.0.bin",
            Sha256Checksum = new string('b', 64),
            ArtifactSizeBytes = 2048,
            IsPublished = true
        };
        var device = new IotDevice
        {
            Id = deviceId,
            DeviceCode = "ESP-FW2",
            DisplayName = "x",
            SiteId = Guid.NewGuid(),
            ApiKeyHash = "h",
            ApiKeyLastFour = "abcd",
            ApiKeyScopes = IotApiKeyScopeEnum.EdgeDeviceDefault,
            TargetFirmwareReleaseId = fwId,
            TargetFirmwareRelease = fw,
            CurrentFirmwareVersion = "1.0.0"
        };

        var uow = new MockUnitOfWorkBuilder()
            .WithIotDevices(device)
            .WithIotFirmwareReleases(fw);
        var handler = new CheckIotFirmwareUpdateQueryHandler(uow.Build());

        var result = await handler.Handle(new CheckIotFirmwareUpdateQuery { DeviceId = deviceId, CurrentVersion = "1.0.0" }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data!.UpdateAvailable.Should().BeTrue();
        result.Data.TargetVersion.Should().Be("1.1.0");
        result.Data.UpdateLogId.Should().NotBeNullOrEmpty();
        uow.IotFirmwareUpdateLogs.Verify(r => r.AddAsync(It.IsAny<IotFirmwareUpdateLog>()), Times.Once);
    }
}
