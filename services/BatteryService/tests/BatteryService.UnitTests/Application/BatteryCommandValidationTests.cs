using BatteryService.Application.CQRS.Command.BatteryAsset;
using BatteryService.Application.CQRS.Command.BatteryGroup;
using BatteryService.Application.CQRS.Command.BatteryType;
using BatteryService.Application.CQRS.Command.Site;
using BatteryService.Application.CQRS.Command.ThresholdConfig;

namespace BatteryService.UnitTests.Application;

public class BatteryCommandValidationTests
{
    [Fact]
    public async Task CreateBatteryAsset_InvalidSerial_ReturnsValidationError()
    {
        var command = new CreateBatteryAssetCommand
        {
            SerialNumber = "bad serial",
            BatteryTypeId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            InstallDate = DateTime.UtcNow.AddDays(-1)
        };

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.ListErrors.Should().Contain(error => error.Field == nameof(command.SerialNumber));
    }

    [Fact]
    public async Task CreateBatteryAsset_FutureInstallDate_ReturnsValidationError()
    {
        var command = new CreateBatteryAssetCommand
        {
            SerialNumber = "BAT-2026-001",
            BatteryTypeId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            InstallDate = DateTime.UtcNow.AddDays(1)
        };

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(error => error.Field == nameof(command.InstallDate));
    }

    [Fact]
    public async Task CreateBatteryType_ValidInput_ReturnsSuccess()
    {
        var command = new CreateBatteryTypeCommand
        {
            Name = "LiFePO4 12V 100Ah",
            NominalCapacityAh = 100,
            NominalVoltage = 12,
            MaxCycleCount = 2000
        };

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeTrue();
        result.ListErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task UpsertThresholdConfig_CriticalSocGreaterThanWarning_ReturnsValidationError()
    {
        var command = new UpsertThresholdConfigCommand
        {
            BatteryTypeId = Guid.NewGuid(),
            VoltageMin = 10,
            VoltageMax = 14,
            TemperatureMin = 0,
            TemperatureMax = 60,
            SocWarningThreshold = 20,
            SocCriticalThreshold = 25
        };

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(error => error.Field == nameof(command.SocCriticalThreshold));
    }

    [Fact]
    public async Task CreateSite_EmptyName_ReturnsValidationError()
    {
        var command = new CreateSiteCommand
        {
            Name = " ",
            CustomerId = Guid.NewGuid(),
            InstallDate = DateTime.UtcNow.AddDays(-1)
        };

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(error => error.Field == nameof(command.Name));
    }

    [Fact]
    public async Task CreateBatteryGroup_MissingSite_ReturnsValidationError()
    {
        var command = new CreateBatteryGroupCommand
        {
            Name = "Block A",
            BatteryTypeId = Guid.NewGuid()
        };

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(error => error.Field == nameof(command.SiteId));
    }
}
