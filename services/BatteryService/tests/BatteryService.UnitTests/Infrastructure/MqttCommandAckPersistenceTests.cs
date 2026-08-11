using BatteryService.Application.Interfaces;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.Infrastructure.Mqtt;
using BatteryService.UnitTests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BatteryService.UnitTests.Infrastructure;

public class MqttCommandAckPersistenceTests
{
    [Fact]
    public async Task AckUpdatesMatchingCommandAndStoresActualState()
    {
        var device = Device();
        var command = Command(device.Id, "cmd-1");
        var builder = new MockUnitOfWorkBuilder()
            .WithIotDevices(device)
            .WithIotDeviceCommands(command);
        await using var provider = Provider(builder.Build());
        var bridge = Bridge(provider);

        await bridge.PersistCommandAckAsync(device.MqttUsername!,
            """{"cmdId":"cmd-1","status":"ok","state":{"serial":"BAT-001","chargeEnabled":true,"dischargeEnabled":false}}""");

        command.Status.Should().Be(IotDeviceCommandStatusEnum.Ok);
        command.ResultJson.Should().Contain("\"chargeEnabled\":true");
        command.AckedAt.Should().NotBeNull();
        builder.IotDeviceCommands.Verify(repo => repo.UpdateAsync(command), Times.Once);
    }

    [Fact]
    public async Task AckWithUnknownCmdIdIsIgnored()
    {
        var device = Device();
        var command = Command(device.Id, "known");
        var builder = new MockUnitOfWorkBuilder()
            .WithIotDevices(device)
            .WithIotDeviceCommands(command);
        await using var provider = Provider(builder.Build());
        var bridge = Bridge(provider);

        await bridge.PersistCommandAckAsync(device.MqttUsername!,
            """{"cmdId":"missing","status":"failed","error":"nope"}""");

        command.Status.Should().Be(IotDeviceCommandStatusEnum.Pending);
        builder.IotDeviceCommands.Verify(repo => repo.UpdateAsync(It.IsAny<IotDeviceCommand>()), Times.Never);
    }

    [Fact]
    public async Task BmsSwitchAckNormalizesLegacyFirmwareErrorToEnglish()
    {
        var device = Device();
        var command = Command(device.Id, "cmd-legacy-error");
        var builder = new MockUnitOfWorkBuilder()
            .WithIotDevices(device)
            .WithIotDeviceCommands(command);
        await using var provider = Provider(builder.Build());
        var bridge = Bridge(provider);

        await bridge.PersistCommandAckAsync(device.MqttUsername!,
            """{"cmdId":"cmd-legacy-error","status":"rejected","error":"legacy device error"}""");

        command.Status.Should().Be(IotDeviceCommandStatusEnum.Rejected);
        command.AckError.Should().Be("The BMS rejected the control command.");
        builder.IotDeviceCommands.Verify(repo => repo.UpdateAsync(command), Times.Once);
    }

    private static ServiceProvider Provider(IBatteryUnitOfWork unitOfWork)
    {
        var services = new ServiceCollection();
        services.AddSingleton(unitOfWork);
        return services.BuildServiceProvider();
    }

    private static MqttBridgeBackgroundService Bridge(ServiceProvider provider) => new(
        provider.GetRequiredService<IServiceScopeFactory>(),
        Options.Create(new MqttOptions()),
        NullLogger<MqttBridgeBackgroundService>.Instance);

    private static IotDevice Device() => new()
    {
        Id = Guid.NewGuid(),
        DeviceCode = "GW-001",
        MqttUsername = "gw-001",
        DisplayName = "Gateway",
        SiteId = Guid.NewGuid(),
        Status = IotDeviceStatusEnum.Active
    };

    private static IotDeviceCommand Command(Guid deviceId, string cmdId) => new()
    {
        Id = Guid.NewGuid(),
        IotDeviceId = deviceId,
        CmdId = cmdId,
        Type = "set_bms_switch",
        ParamsJson = "{}",
        Status = IotDeviceCommandStatusEnum.Pending
    };
}
