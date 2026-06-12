using BatteryService.Application.Anomaly;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using FluentAssertions;

namespace BatteryService.UnitTests.Application;

/// <summary>
/// Sprint 5B #93 + #105 — unit tests cho ambient + Tier 2 anomaly detection.
/// Format: pure data → AnomalyRules.Detect/DetectAmbient → assert result.
/// </summary>
public class AnomalyRulesSprint5BTests
{
    // ===== #105 Tier 2 — Internal Resistance =====

    [Fact]
    public void Detect_HighInternalResistance_OverThreshold_ShouldRaiseCritical()
    {
        var reading = BaseReading();
        reading.InternalResistanceMilliohm = 50.5m;

        var threshold = BaseThreshold();
        threshold.InternalResistanceMaxMilliohm = 40m;

        var result = AnomalyRules.Detect(reading, threshold);

        result.Should().ContainSingle(a =>
            a.Type == AnomalyTypeEnum.HighInternalResistance &&
            a.Severity == AlertSeverityEnum.Critical &&
            a.ActualValue == 50.5m &&
            a.ThresholdValue == 40m &&
            a.Unit == "mΩ");
    }

    [Fact]
    public void Detect_InternalResistance_AtThreshold_ShouldNotRaise()
    {
        var reading = BaseReading();
        reading.InternalResistanceMilliohm = 40m;

        var threshold = BaseThreshold();
        threshold.InternalResistanceMaxMilliohm = 40m;

        var result = AnomalyRules.Detect(reading, threshold);

        result.Should().NotContain(a => a.Type == AnomalyTypeEnum.HighInternalResistance);
    }

    [Fact]
    public void Detect_InternalResistance_Null_ShouldNotRaise()
    {
        var reading = BaseReading();
        reading.InternalResistanceMilliohm = null;

        var threshold = BaseThreshold();
        threshold.InternalResistanceMaxMilliohm = 40m;

        var result = AnomalyRules.Detect(reading, threshold);

        result.Should().NotContain(a => a.Type == AnomalyTypeEnum.HighInternalResistance);
    }

    // ===== #105 Tier 2 — Cell Voltage Delta =====

    [Fact]
    public void Detect_CellImbalance_OverThreshold_ShouldRaiseCritical()
    {
        var reading = BaseReading();
        reading.CellVoltageDeltaMv = 150m;

        var threshold = BaseThreshold();
        threshold.CellVoltageDeltaMaxMv = 100m;

        var result = AnomalyRules.Detect(reading, threshold);

        result.Should().ContainSingle(a =>
            a.Type == AnomalyTypeEnum.CellImbalance &&
            a.Severity == AlertSeverityEnum.Critical &&
            a.ActualValue == 150m &&
            a.Unit == "mV");
    }

    [Fact]
    public void Detect_CellImbalance_Null_ShouldNotRaise()
    {
        var reading = BaseReading();
        reading.CellVoltageDeltaMv = null;

        var threshold = BaseThreshold();
        threshold.CellVoltageDeltaMaxMv = 100m;

        var result = AnomalyRules.Detect(reading, threshold);

        result.Should().NotContain(a => a.Type == AnomalyTypeEnum.CellImbalance);
    }

    // ===== #93 Ambient — Temperature =====

    [Fact]
    public void DetectAmbient_HighTemp_AboveCritical_ShouldRaiseCritical()
    {
        var reading = MakeAmbient(temp: 45m, humidity: 50m);
        var threshold = MakeAmbientThreshold(
            tempWarning: 35m, tempCritical: 40m);

        var result = AnomalyRules.DetectAmbient(reading, threshold);

        result.Should().ContainSingle(a =>
            a.Type == AnomalyTypeEnum.HighAmbientTemp &&
            a.Severity == AlertSeverityEnum.Critical);
    }

    [Fact]
    public void DetectAmbient_HighTemp_BetweenWarningAndCritical_ShouldRaiseWarning()
    {
        var reading = MakeAmbient(temp: 37m, humidity: 50m);
        var threshold = MakeAmbientThreshold(
            tempWarning: 35m, tempCritical: 40m);

        var result = AnomalyRules.DetectAmbient(reading, threshold);

        result.Should().ContainSingle(a =>
            a.Type == AnomalyTypeEnum.HighAmbientTemp &&
            a.Severity == AlertSeverityEnum.Warning);
    }

    [Fact]
    public void DetectAmbient_Temp_BelowWarning_ShouldNotRaise()
    {
        var reading = MakeAmbient(temp: 30m, humidity: 50m);
        var threshold = MakeAmbientThreshold(
            tempWarning: 35m, tempCritical: 40m);

        var result = AnomalyRules.DetectAmbient(reading, threshold);

        result.Should().NotContain(a => a.Type == AnomalyTypeEnum.HighAmbientTemp);
    }

    // ===== #93 Ambient — Humidity =====

    [Fact]
    public void DetectAmbient_HighHumidity_AboveCritical_ShouldRaiseCritical()
    {
        var reading = MakeAmbient(temp: 25m, humidity: 95m);
        var threshold = MakeAmbientThreshold(
            humidityWarning: 75m, humidityCritical: 90m);

        var result = AnomalyRules.DetectAmbient(reading, threshold);

        result.Should().ContainSingle(a =>
            a.Type == AnomalyTypeEnum.HighHumidity &&
            a.Severity == AlertSeverityEnum.Critical);
    }

    [Fact]
    public void DetectAmbient_HighHumidity_BetweenWarningCritical_ShouldRaiseWarning()
    {
        var reading = MakeAmbient(temp: 25m, humidity: 80m);
        var threshold = MakeAmbientThreshold(
            humidityWarning: 75m, humidityCritical: 90m);

        var result = AnomalyRules.DetectAmbient(reading, threshold);

        result.Should().ContainSingle(a =>
            a.Type == AnomalyTypeEnum.HighHumidity &&
            a.Severity == AlertSeverityEnum.Warning);
    }

    // ===== #93 Ambient — Combo =====

    [Fact]
    public void DetectAmbient_Combo_BothExceed_ShouldRaiseComboCritical()
    {
        var reading = MakeAmbient(temp: 35m, humidity: 80m);
        var threshold = MakeAmbientThreshold(
            tempWarning: 30m, tempCritical: 38m,
            humidityWarning: 70m, humidityCritical: 85m,
            comboTemp: 33m, comboHumidity: 75m);

        var result = AnomalyRules.DetectAmbient(reading, threshold);

        result.Should().Contain(a => a.Type == AnomalyTypeEnum.HighTempHumidityCombo);
    }

    [Fact]
    public void DetectAmbient_Combo_OnlyTempExceeds_ShouldNotRaiseCombo()
    {
        var reading = MakeAmbient(temp: 34m, humidity: 60m);
        var threshold = MakeAmbientThreshold(
            tempWarning: 30m, comboTemp: 33m, comboHumidity: 75m);

        var result = AnomalyRules.DetectAmbient(reading, threshold);

        result.Should().NotContain(a => a.Type == AnomalyTypeEnum.HighTempHumidityCombo);
    }

    [Fact]
    public void DetectAmbient_Disabled_ShouldReturnEmpty()
    {
        var reading = MakeAmbient(temp: 99m, humidity: 99m);
        var threshold = MakeAmbientThreshold(
            tempCritical: 30m, humidityCritical: 80m);
        threshold.Enabled = false;

        var result = AnomalyRules.DetectAmbient(reading, threshold);

        result.Should().BeEmpty();
    }

    // ===== Helpers =====

    private static SensorReading BaseReading() => new()
    {
        Time = DateTime.UtcNow,
        BatteryAssetId = Guid.NewGuid(),
        Voltage = 3.7m,
        Current = 1m,
        Temperature = 25m,
        SocPercent = 80m
    };

    private static ThresholdConfig BaseThreshold() => new()
    {
        BatteryTypeId = Guid.NewGuid(),
        VoltageMin = 2.5m,
        VoltageMax = 4.2m,
        TemperatureMax = 60m,
        TemperatureMin = -10m,
        SocWarningThreshold = 20m,
        SocCriticalThreshold = 10m,
        EffectiveFromUtc = DateTime.UtcNow,
        IsActive = true
    };

    private static AmbientReading MakeAmbient(decimal temp, decimal humidity) => new()
    {
        Time = DateTime.UtcNow,
        SiteId = Guid.NewGuid(),
        AmbientTemperature = temp,
        Humidity = humidity,
        Source = AmbientReadingSourceEnum.IotSensor
    };

    private static AmbientThresholdConfig MakeAmbientThreshold(
        decimal? tempWarning = null,
        decimal? tempCritical = null,
        decimal? humidityWarning = null,
        decimal? humidityCritical = null,
        decimal? comboTemp = null,
        decimal? comboHumidity = null) => new()
        {
            SiteId = Guid.NewGuid(),
            HighAmbientTempWarning = tempWarning,
            HighAmbientTempCritical = tempCritical,
            HighHumidityWarning = humidityWarning,
            HighHumidityCritical = humidityCritical,
            ComboTempThreshold = comboTemp,
            ComboHumidityThreshold = comboHumidity,
            Enabled = true
        };
}
