using BatteryService.Application.CQRS.Command.BatteryAsset;
using BatteryService.Application.CQRS.Handler.BatteryAsset;
using BatteryService.Application.CQRS.Query.BatteryAsset;
using BatteryService.Application.Services;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.UnitTests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BatteryService.UnitTests.Application;

public class BmsSwitchCommandHandlerTests
{
    [Fact]
    public async Task SwitchCommand_IsAcceptedAndAudited()
    {
        var customerId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var asset = Asset(customerId, siteId);
        var device = Device(siteId);
        var builder = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(asset)
            .WithIotDevices(device);
        var mqtt = new Mock<IMqttBridgePublisher>();

        var result = await Handler(builder, TestBatteryCurrentUserService.Customer(customerId), mqtt.Object)
            .Handle(new SetBmsSwitchCommand
            {
                BatteryAssetId = asset.Id,
                Target = "charge",
                Enable = false
            }, CancellationToken.None);

        result.StatusCode.Should().Be(202);
        result.IsSuccess.Should().BeTrue();
        builder.IotDeviceCommands.Verify(repo => repo.AddAsync(It.Is<IotDeviceCommand>(command =>
            command.BatteryAssetId == asset.Id
            && command.IssuedByAccountId == customerId
            && command.Status == IotDeviceCommandStatusEnum.Pending)), Times.Once);
        mqtt.Verify(publisher => publisher.PublishCommandAsync(
            device.DeviceCode,
            It.Is<string>(payload => payload.Contains("set_bms_switch")
                                     && payload.Contains(asset.SerialNumber)
                                     && payload.Contains("\"target\":\"charge\"")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("charge")]
    [InlineData("discharge")]
    public async Task SupportedTargets_ArePublishedToDevice(string target)
    {
        var customerId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var asset = Asset(customerId, siteId);
        var device = Device(siteId);
        var builder = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(asset)
            .WithIotDevices(device);
        var mqtt = new Mock<IMqttBridgePublisher>();

        var result = await Handler(builder, TestBatteryCurrentUserService.Customer(customerId), mqtt.Object)
            .Handle(new SetBmsSwitchCommand
            {
                BatteryAssetId = asset.Id,
                Target = target,
                Enable = true
            }, CancellationToken.None);

        result.StatusCode.Should().Be(202);
        mqtt.Verify(publisher => publisher.PublishCommandAsync(
            device.DeviceCode,
            It.Is<string>(payload => payload.Contains($"\"target\":\"{target}\"")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("both")]
    [InlineData("all")]
    [InlineData("")]
    [InlineData("CHARGE_MOSFET")]
    public async Task UnsupportedTarget_IsRejectedBeforeReachingDevice(string target)
    {
        var customerId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var asset = Asset(customerId, siteId);
        var builder = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(asset)
            .WithIotDevices(Device(siteId));
        var mqtt = new Mock<IMqttBridgePublisher>();

        var result = await Handler(builder, TestBatteryCurrentUserService.Customer(customerId), mqtt.Object)
            .Handle(new SetBmsSwitchCommand
            {
                BatteryAssetId = asset.Id,
                Target = target,
                Enable = true
            }, CancellationToken.None);

        result.StatusCode.Should().Be(400);
        mqtt.Verify(publisher => publisher.PublishCommandAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CustomerNonOwner_IsHiddenAsNotFound()
    {
        var ownerId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var asset = Asset(ownerId, siteId);
        var builder = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(asset)
            .WithIotDevices(Device(siteId));

        var result = await Handler(builder, TestBatteryCurrentUserService.Customer(Guid.NewGuid()),
                Mock.Of<IMqttBridgePublisher>())
            .Handle(new SetBmsSwitchCommand
            {
                BatteryAssetId = asset.Id,
                Target = "charge",
                Enable = false
            }, CancellationToken.None);

        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task MultipleActiveGateways_ReturnsConflict()
    {
        var customerId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var asset = Asset(customerId, siteId);
        var builder = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(asset)
            .WithIotDevices(Device(siteId), Device(siteId));

        var result = await Handler(builder, TestBatteryCurrentUserService.Customer(customerId),
                Mock.Of<IMqttBridgePublisher>())
            .Handle(new SetBmsSwitchCommand
            {
                BatteryAssetId = asset.Id,
                Target = "discharge",
                Enable = true
            }, CancellationToken.None);

        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task PendingCommand_ReturnsConflict()
    {
        var customerId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var asset = Asset(customerId, siteId);
        var device = Device(siteId);
        var pending = new IotDeviceCommand
        {
            Id = Guid.NewGuid(),
            IotDeviceId = device.Id,
            BatteryAssetId = asset.Id,
            CmdId = "pending-1",
            Type = "set_bms_switch",
            ParamsJson = "{\"serial\":\"BAT-001\",\"target\":\"charge\",\"enable\":true}",
            Status = IotDeviceCommandStatusEnum.Pending
        };
        var builder = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(asset)
            .WithIotDevices(device)
            .WithIotDeviceCommands(pending);

        var result = await Handler(builder, TestBatteryCurrentUserService.Customer(customerId),
                Mock.Of<IMqttBridgePublisher>())
            .Handle(new SetBmsSwitchCommand
            {
                BatteryAssetId = asset.Id,
                Target = "charge",
                Enable = false
            }, CancellationToken.None);

        result.StatusCode.Should().Be(409);
    }

    private static SetBmsSwitchCommandHandler Handler(
        MockUnitOfWorkBuilder builder,
        TestBatteryCurrentUserService currentUser,
        IMqttBridgePublisher publisher) => new(
            builder.Build(),
            currentUser,
            publisher,
            NullLogger<SetBmsSwitchCommandHandler>.Instance);

    private static BatteryAsset Asset(Guid customerId, Guid siteId) => new()
    {
        Id = Guid.NewGuid(),
        CustomerId = customerId,
        SiteId = siteId,
        SerialNumber = "BAT-001"
    };

    private static IotDevice Device(Guid siteId) => new()
    {
        Id = Guid.NewGuid(),
        SiteId = siteId,
        DeviceCode = $"GW-{Guid.NewGuid():N}",
        DisplayName = "Gateway",
        Status = IotDeviceStatusEnum.Active
    };
}

public class BmsSwitchStateQueryHandlerTests
{
    [Fact]
    public async Task ReturnsLastReadBackStateAndPendingCommand()
    {
        var customerId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var asset = new BatteryAsset
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            SiteId = siteId,
            SerialNumber = "BAT-001"
        };
        var device = new IotDevice
        {
            Id = Guid.NewGuid(),
            SiteId = siteId,
            DeviceCode = "GW-001",
            DisplayName = "Gateway",
            Status = IotDeviceStatusEnum.Active
        };
        var verified = new IotDeviceCommand
        {
            Id = Guid.NewGuid(),
            IotDeviceId = device.Id,
            BatteryAssetId = asset.Id,
            CmdId = "verified",
            Type = "set_bms_switch",
            ParamsJson = "{\"target\":\"charge\",\"enable\":true}",
            ResultJson = "{\"serial\":\"BAT-001\",\"chargeEnabled\":true,\"dischargeEnabled\":false}",
            Status = IotDeviceCommandStatusEnum.Ok,
            CreatedAt = DateTime.UtcNow.AddSeconds(-10),
            AckedAt = DateTime.UtcNow.AddSeconds(-9)
        };
        var pending = new IotDeviceCommand
        {
            Id = Guid.NewGuid(),
            IotDeviceId = device.Id,
            BatteryAssetId = asset.Id,
            CmdId = "pending",
            Type = "set_bms_switch",
            ParamsJson = "{\"target\":\"discharge\",\"enable\":true}",
            Status = IotDeviceCommandStatusEnum.Pending,
            CreatedAt = DateTime.UtcNow
        };
        var builder = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(asset)
            .WithIotDevices(device)
            .WithIotDeviceCommands(verified, pending);
        var handler = new GetBmsSwitchStateQueryHandler(
            builder.Build(), TestBatteryCurrentUserService.Customer(customerId));

        var result = await handler.Handle(
            new GetBmsSwitchStateQuery { BatteryAssetId = asset.Id }, CancellationToken.None);

        result.StatusCode.Should().Be(200);
        result.Data!.ChargeEnabled.Should().BeTrue();
        result.Data.DischargeEnabled.Should().BeFalse();
        result.Data.PendingCommand!.CmdId.Should().Be("pending");
        result.Data.PendingCommand.Target.Should().Be("discharge");
    }
}
