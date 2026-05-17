using BatteryService.Application.CQRS.Command.SensorReading;
using BatteryService.Application.CQRS.Command.ThresholdConfig;
using BatteryService.Domain.Enums;

namespace BatteryService.UnitTests.Application;

public class Sprint3ValidationTests
{
    private static SensorReadingItem ValidItem() => new()
    {
        BatteryAssetId = Guid.NewGuid(),
        Time = DateTime.UtcNow,
        Voltage = 12m,
        Current = 1m,
        Temperature = 25m,
        SocPercent = 50m
    };

    private static UpsertThresholdConfigCommand ValidThreshold() => new()
    {
        BatteryTypeId = Guid.NewGuid(),
        VoltageMin = 10m,
        VoltageMax = 14m,
        TemperatureMin = -10m,
        TemperatureMax = 50m,
        SocWarningThreshold = 20m,
        SocCriticalThreshold = 10m
    };

    // ===== BatchIngest SOH validation =====
    [Fact]
    public async Task BatchIngest_SohBelow0_Error()
    {
        var item = ValidItem();
        item.SohPercent = -1m;
        var cmd = new BatchIngestSensorReadingsCommand { Items = new() { item } };
        var r = await cmd.ValidateAsync();
        r.ListErrors.Should().Contain(e => e.Field.EndsWith(".SohPercent"));
    }

    [Fact]
    public async Task BatchIngest_SohAbove100_Error()
    {
        var item = ValidItem();
        item.SohPercent = 101m;
        var cmd = new BatchIngestSensorReadingsCommand { Items = new() { item } };
        var r = await cmd.ValidateAsync();
        r.ListErrors.Should().Contain(e => e.Field.EndsWith(".SohPercent"));
    }

    [Fact]
    public async Task BatchIngest_SohInRange_OK()
    {
        var item = ValidItem();
        item.SohPercent = 85m;
        var cmd = new BatchIngestSensorReadingsCommand { Items = new() { item } };
        var r = await cmd.ValidateAsync();
        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task BatchIngest_InvalidChargingState_Error()
    {
        var item = ValidItem();
        item.ChargingState = (ChargingStateEnum)99; // không nằm trong định nghĩa
        var cmd = new BatchIngestSensorReadingsCommand { Items = new() { item } };
        var r = await cmd.ValidateAsync();
        r.ListErrors.Should().Contain(e => e.Field.EndsWith(".ChargingState"));
    }

    [Fact]
    public async Task BatchIngest_ValidChargingState_OK()
    {
        var item = ValidItem();
        item.ChargingState = ChargingStateEnum.Charging;
        var cmd = new BatchIngestSensorReadingsCommand { Items = new() { item } };
        var r = await cmd.ValidateAsync();
        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task BatchIngest_NullSohAndChargingState_OK_BecauseNullable()
    {
        var item = ValidItem();
        item.SohPercent = null;
        item.ChargingState = null;
        var cmd = new BatchIngestSensorReadingsCommand { Items = new() { item } };
        var r = await cmd.ValidateAsync();
        r.IsSuccess.Should().BeTrue();
    }

    // ===== UpsertThresholdConfig SOH validation =====
    [Fact]
    public async Task UpsertThreshold_SohWarningBelow0_Error()
    {
        var cmd = ValidThreshold();
        cmd.SohWarningThreshold = -5m;
        var r = await cmd.ValidateAsync();
        r.ListErrors.Should().Contain(e => e.Field == nameof(cmd.SohWarningThreshold));
    }

    [Fact]
    public async Task UpsertThreshold_SohCriticalAbove100_Error()
    {
        var cmd = ValidThreshold();
        cmd.SohCriticalThreshold = 101m;
        var r = await cmd.ValidateAsync();
        r.ListErrors.Should().Contain(e => e.Field == nameof(cmd.SohCriticalThreshold));
    }

    [Fact]
    public async Task UpsertThreshold_SohCriticalGreaterOrEqualWarning_Error()
    {
        var cmd = ValidThreshold();
        cmd.SohWarningThreshold = 80m;
        cmd.SohCriticalThreshold = 85m;
        var r = await cmd.ValidateAsync();
        r.ListErrors.Should().Contain(e => e.Field == nameof(cmd.SohCriticalThreshold));
    }

    [Fact]
    public async Task UpsertThreshold_SohValidOrdering_OK()
    {
        var cmd = ValidThreshold();
        cmd.SohWarningThreshold = 85m;
        cmd.SohCriticalThreshold = 75m;
        var r = await cmd.ValidateAsync();
        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task UpsertThreshold_NullSohOptional_OK()
    {
        var cmd = ValidThreshold();
        cmd.SohWarningThreshold = null;
        cmd.SohCriticalThreshold = null;
        var r = await cmd.ValidateAsync();
        r.IsSuccess.Should().BeTrue();
    }
}
