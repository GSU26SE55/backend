using BatteryService.Application.CQRS.Command.IotDevice;
using BatteryService.Application.CQRS.Command.IotFirmware;
using BatteryService.Application.CQRS.Handler.IotDevice;
using BatteryService.Application.CQRS.Handler.IotFirmware;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.Infrastructure.Implements.Services;
using BatteryService.UnitTests.Helpers;

namespace BatteryService.UnitTests.Application;

/// <summary>
/// Khoá chặt response contract:
/// - 401: thiếu token (do middleware ASP.NET — không test ở handler).
/// - 403: permission/role (do [Authorize] — không test ở handler).
/// - 400: field validation → <c>ListErrors</c> populated.
/// - 404: not found in DB.
/// - 409: state conflict (Disabled, revoked, duplicate, archived, not-published).
/// - 422: business rule violation (clock skew, etc.).
/// - <c>ListErrors</c> chỉ có entry khi 400 (field-level). Status khác → list rỗng → serialize null.
/// </summary>
public class IotResponseContractTests
{
    private static IotDevice DisabledDevice() => new()
    {
        Id = Guid.NewGuid(),
        DeviceCode = "ESP-DISABLED",
        DisplayName = "x",
        SiteId = Guid.NewGuid(),
        Status = IotDeviceStatusEnum.Disabled,
        ApiKeyHash = "h",
        ApiKeyLastFour = "abcd",
        ApiKeyScopes = IotApiKeyScopeEnum.EdgeDeviceDefault,
        HeartbeatIntervalSeconds = 60
    };

    [Fact]
    public async Task Provision_Returns409_WhenDeviceDisabled()
    {
        var device = DisabledDevice();
        var uow = new MockUnitOfWorkBuilder().WithIotDevices(device);
        var handler = new ProvisionIotDeviceCommandHandler(uow.Build());

        var result = await handler.Handle(new ProvisionIotDeviceCommand
        {
            DeviceId = device.Id,
            DeviceCode = device.DeviceCode,
            FirmwareVersion = "1.0.0",
            DeviceTimestamp = DateTime.UtcNow
        }, default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        result.ListErrors.Should().BeEmpty("status 409 không phải field-validation; ListErrors phải rỗng");
        result.Message.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Provision_Returns403_WhenDeviceCodeMismatch()
    {
        var device = new IotDevice
        {
            Id = Guid.NewGuid(),
            DeviceCode = "REAL-CODE",
            DisplayName = "x",
            SiteId = Guid.NewGuid(),
            Status = IotDeviceStatusEnum.Pending,
            ApiKeyHash = "h",
            ApiKeyLastFour = "abcd",
            ApiKeyScopes = IotApiKeyScopeEnum.EdgeDeviceDefault,
            HeartbeatIntervalSeconds = 60
        };
        var uow = new MockUnitOfWorkBuilder().WithIotDevices(device);
        var handler = new ProvisionIotDeviceCommandHandler(uow.Build());

        var result = await handler.Handle(new ProvisionIotDeviceCommand
        {
            DeviceId = device.Id,
            DeviceCode = "FAKE-CODE", // mismatch
            FirmwareVersion = "1.0.0",
            DeviceTimestamp = DateTime.UtcNow
        }, default);

        result.StatusCode.Should().Be(403);
        result.ListErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task Provision_Returns404_WhenDeviceMissing()
    {
        var uow = new MockUnitOfWorkBuilder();
        var handler = new ProvisionIotDeviceCommandHandler(uow.Build());

        var result = await handler.Handle(new ProvisionIotDeviceCommand
        {
            DeviceId = Guid.NewGuid(),
            DeviceCode = "X",
            FirmwareVersion = "1.0.0",
            DeviceTimestamp = DateTime.UtcNow
        }, default);

        result.StatusCode.Should().Be(404);
        result.ListErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task Provision_Returns422_WhenClockSkewBeyondThreshold()
    {
        var device = DisabledDevice();
        device.Status = IotDeviceStatusEnum.Pending;
        var uow = new MockUnitOfWorkBuilder().WithIotDevices(device);
        var handler = new ProvisionIotDeviceCommandHandler(uow.Build());

        var result = await handler.Handle(new ProvisionIotDeviceCommand
        {
            DeviceId = device.Id,
            DeviceCode = device.DeviceCode,
            FirmwareVersion = "1.0.0",
            DeviceTimestamp = DateTime.UtcNow.AddMinutes(10)
        }, default);

        result.StatusCode.Should().Be(422);
        result.ListErrors.Should().BeEmpty("clock skew là business rule violation, không phải field-validation");
    }

    [Fact]
    public async Task Heartbeat_Returns409_WhenRevoked()
    {
        var device = new IotDevice
        {
            Id = Guid.NewGuid(),
            DeviceCode = "ESP-REVOKED",
            DisplayName = "x",
            SiteId = Guid.NewGuid(),
            Status = IotDeviceStatusEnum.Active,
            ApiKeyHash = "h",
            ApiKeyLastFour = "abcd",
            ApiKeyScopes = IotApiKeyScopeEnum.EdgeDeviceDefault,
            ApiKeyRevokedAt = DateTime.UtcNow.AddMinutes(-1),
            HeartbeatIntervalSeconds = 60
        };
        var uow = new MockUnitOfWorkBuilder().WithIotDevices(device);
        var handler = new IotDeviceHeartbeatCommandHandler(uow.Build(), new BatteryService.UnitTests.Helpers.NoopIotMetricsRecorder());

        var result = await handler.Handle(new IotDeviceHeartbeatCommand
        {
            DeviceId = device.Id,
            DeviceCode = device.DeviceCode,
            DeviceTimestamp = DateTime.UtcNow
        }, default);

        result.StatusCode.Should().Be(409);
        result.ListErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateDevice_Returns404_WhenFirmwareTrulyMissing()
    {
        var siteId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var uow = new MockUnitOfWorkBuilder()
            .WithSites(new Site { Id = siteId, Name = "S", CustomerId = Guid.NewGuid() })
            .WithIotDevices(new IotDevice
            {
                Id = deviceId,
                DeviceCode = "DC",
                DisplayName = "x",
                SiteId = siteId,
                ApiKeyHash = "h",
                ApiKeyLastFour = "abcd",
                ApiKeyScopes = IotApiKeyScopeEnum.EdgeDeviceDefault
            });
        var handler = new UpdateIotDeviceCommandHandler(uow.Build());

        var result = await handler.Handle(new UpdateIotDeviceCommand
        {
            Id = deviceId,
            DisplayName = "x",
            SiteId = siteId,
            ApiKeyScopes = IotApiKeyScopeEnum.EdgeDeviceDefault,
            HeartbeatIntervalSeconds = 60,
            Status = IotDeviceStatusEnum.Active,
            TargetFirmwareReleaseId = Guid.NewGuid()
        }, default);

        result.StatusCode.Should().Be(404);
        result.ListErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateDevice_Returns409_WhenFirmwareNotPublished()
    {
        var siteId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var fwId = Guid.NewGuid();
        var uow = new MockUnitOfWorkBuilder()
            .WithSites(new Site { Id = siteId, Name = "S", CustomerId = Guid.NewGuid() })
            .WithIotFirmwareReleases(new IotFirmwareRelease
            {
                Id = fwId,
                Version = "1.0.0",
                HardwareRevision = "v1",
                ArtifactUrl = "https://x",
                Sha256Checksum = new string('c', 64),
                ArtifactSizeBytes = 1,
                IsPublished = false // unpublished
            })
            .WithIotDevices(new IotDevice
            {
                Id = deviceId,
                DeviceCode = "DC",
                DisplayName = "x",
                SiteId = siteId,
                ApiKeyHash = "h",
                ApiKeyLastFour = "abcd",
                ApiKeyScopes = IotApiKeyScopeEnum.EdgeDeviceDefault
            });
        var handler = new UpdateIotDeviceCommandHandler(uow.Build());

        var result = await handler.Handle(new UpdateIotDeviceCommand
        {
            Id = deviceId,
            DisplayName = "x",
            SiteId = siteId,
            ApiKeyScopes = IotApiKeyScopeEnum.EdgeDeviceDefault,
            HeartbeatIntervalSeconds = 60,
            Status = IotDeviceStatusEnum.Active,
            TargetFirmwareReleaseId = fwId
        }, default);

        result.StatusCode.Should().Be(409);
        result.ListErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateDevice_Returns409_WhenDeviceCodeDuplicate_ListErrorsEmpty()
    {
        var uow = new MockUnitOfWorkBuilder();
        var siteId = Guid.NewGuid();
        uow.WithSites(new Site { Id = siteId, Name = "S", CustomerId = Guid.NewGuid() })
           .WithIotDevices(new IotDevice
           {
               Id = Guid.NewGuid(),
               DeviceCode = "ESP-DUP",
               DisplayName = "ex",
               SiteId = siteId,
               ApiKeyHash = "h",
               ApiKeyLastFour = "abcd",
               ApiKeyScopes = IotApiKeyScopeEnum.EdgeDeviceDefault
           });
        var apiKeyService = new IotApiKeyService(uow.Build());
        var handler = new CreateIotDeviceCommandHandler(uow.Build(), apiKeyService, TestMqttBrokerEndpointProvider.Enabled());

        var result = await handler.Handle(new CreateIotDeviceCommand
        {
            DeviceCode = "ESP-DUP",
            DisplayName = "another",
            SiteId = siteId,
            ApiKeyScopes = IotApiKeyScopeEnum.EdgeDeviceDefault
        }, default);

        result.StatusCode.Should().Be(409);
        result.ListErrors.Should().BeEmpty("conflict không phải field-validation");
    }

    [Fact]
    public async Task CreateFirmware_Validate_Returns400_WithListErrors_OnInvalidField()
    {
        // ValidateAsync gọi trực tiếp (pipeline behavior mock).
        var cmd = new CreateIotFirmwareReleaseCommand
        {
            Version = "BAD",      // không phải SemVer
            HardwareRevision = "",
            ArtifactUrl = "not-a-url",
            Sha256Checksum = "short",
            ArtifactSizeBytes = -1
        };
        var response = await cmd.ValidateAsync();

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(400);
        response.ListErrors.Should().NotBeEmpty("field-validation error PHẢI có ListErrors");
        response.ListErrors.Should().Contain(e => e.Field == nameof(cmd.Version));
        response.ListErrors.Should().Contain(e => e.Field == nameof(cmd.HardwareRevision));
        response.ListErrors.Should().Contain(e => e.Field == nameof(cmd.ArtifactUrl));
        response.ListErrors.Should().Contain(e => e.Field == nameof(cmd.Sha256Checksum));
        response.ListErrors.Should().Contain(e => e.Field == nameof(cmd.ArtifactSizeBytes));
    }

    [Fact]
    public async Task PublishFirmware_Returns404_WhenMissing()
    {
        var uow = new MockUnitOfWorkBuilder();
        var handler = new PublishIotFirmwareReleaseCommandHandler(uow.Build());
        var result = await handler.Handle(new PublishIotFirmwareReleaseCommand { Id = Guid.NewGuid() }, default);
        result.StatusCode.Should().Be(404);
        result.ListErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task PublishFirmware_Returns409_WhenArchived()
    {
        var fwId = Guid.NewGuid();
        var uow = new MockUnitOfWorkBuilder()
            .WithIotFirmwareReleases(new IotFirmwareRelease
            {
                Id = fwId,
                Version = "1.0.0",
                HardwareRevision = "v1",
                ArtifactUrl = "https://x",
                Sha256Checksum = new string('d', 64),
                ArtifactSizeBytes = 1,
                IsArchived = true
            });
        var handler = new PublishIotFirmwareReleaseCommandHandler(uow.Build());
        var result = await handler.Handle(new PublishIotFirmwareReleaseCommand { Id = fwId }, default);
        result.StatusCode.Should().Be(409);
        result.ListErrors.Should().BeEmpty();
    }
}
