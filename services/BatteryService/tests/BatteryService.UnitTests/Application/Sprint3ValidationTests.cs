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
    // Sprint IoT-2 #IoT2-17 — SOH ngoài [0..100] đã chuyển từ field-validation (400) sang
    // outlier reject (handler-level + metric reason=sensor_outlier). ValidateAsync KHÔNG còn check SOH.
    // 2 test cũ `BatchIngest_SohBelow0_Error` / `BatchIngest_SohAbove100_Error` đã xoá vì
    // không còn relevant — hành vi mới được test trong IotDeviceLifecycleHandlerTests.BatchIngest_RejectsOutlierVoltage
    // và spec acceptance check (51 outlier voltage → device decommission).

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
