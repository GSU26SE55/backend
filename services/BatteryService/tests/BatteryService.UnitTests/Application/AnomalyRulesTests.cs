using BatteryService.Application.Anomaly;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;

namespace BatteryService.UnitTests.Application;

public class AnomalyRulesTests
{
    private static ThresholdConfig Threshold() => new()
    {
        Id = Guid.NewGuid(),
        BatteryTypeId = Guid.NewGuid(),
        VoltageMin = 10m,
        VoltageMax = 14m,
        TemperatureMin = -10m,
        TemperatureMax = 50m,
        SocWarningThreshold = 20m,
        SocCriticalThreshold = 10m,
        CurrentMaxCharge = 10m,
        CurrentMaxDischarge = 10m,
        SohWarningThreshold = 85m,
        SohCriticalThreshold = 75m,
        EffectiveFromUtc = DateTime.UtcNow,
        IsActive = true
    };

    private static SensorReading Reading(decimal voltage = 12m, decimal current = 0m, decimal temp = 25m, decimal soc = 50m, decimal? soh = null) => new()
    {
        Time = DateTime.UtcNow,
        BatteryAssetId = Guid.NewGuid(),
        Voltage = voltage,
        Current = current,
        Temperature = temp,
        SocPercent = soc,
        SohPercent = soh
    };

    [Fact]
    public void NoAnomaly_WhenAllInRange()
        => AnomalyRules.Detect(Reading(), Threshold()).Should().BeEmpty();

    [Fact]
    public void Overheat_Warning_WhenAboveButWithin5C()
        => AnomalyRules.Detect(Reading(temp: 53m), Threshold())
            .Should().ContainSingle(a => a.Type == AnomalyTypeEnum.Overheat && a.Severity == AlertSeverityEnum.Warning);

    [Fact]
    public void Overheat_Critical_WhenBeyond5C()
        => AnomalyRules.Detect(Reading(temp: 60m), Threshold())
            .Should().ContainSingle(a => a.Type == AnomalyTypeEnum.Overheat && a.Severity == AlertSeverityEnum.Critical);

    [Fact]
    public void Overvoltage_Critical()
        => AnomalyRules.Detect(Reading(voltage: 15m), Threshold())
            .Should().ContainSingle(a => a.Type == AnomalyTypeEnum.Overvoltage && a.Severity == AlertSeverityEnum.Critical);

    [Fact]
    public void Undervoltage_Critical()
        => AnomalyRules.Detect(Reading(voltage: 9m), Threshold())
            .Should().ContainSingle(a => a.Type == AnomalyTypeEnum.Undervoltage);

    [Fact]
    public void LowSoc_Warning_WhenBelowWarning_ButAboveCritical()
        => AnomalyRules.Detect(Reading(soc: 15m), Threshold())
            .Should().ContainSingle(a => a.Type == AnomalyTypeEnum.LowSoc && a.Severity == AlertSeverityEnum.Warning);

    [Fact]
    public void LowSoc_Critical_WhenBelowCritical()
        => AnomalyRules.Detect(Reading(soc: 5m), Threshold())
            .Should().ContainSingle(a => a.Type == AnomalyTypeEnum.LowSoc && a.Severity == AlertSeverityEnum.Critical);

    [Fact]
    public void RapidDischarge_Critical_WhenAbsCurrentAboveLimit()
        => AnomalyRules.Detect(Reading(current: -15m), Threshold())
            .Should().ContainSingle(a => a.Type == AnomalyTypeEnum.RapidDischarge && a.ActualValue == 15m);

    [Fact]
    public void AbnormalCharging_Critical_WhenCurrentAboveLimit()
        => AnomalyRules.Detect(Reading(current: 12m), Threshold())
            .Should().ContainSingle(a => a.Type == AnomalyTypeEnum.AbnormalCharging);

    [Fact]
    public void SohDegradation_Warning_WhenBelowWarning()
        => AnomalyRules.Detect(Reading(soh: 80m), Threshold())
            .Should().ContainSingle(a => a.Type == AnomalyTypeEnum.SohDegradation && a.Severity == AlertSeverityEnum.Warning);

    [Fact]
    public void SohDegradation_Critical_WhenBelowCritical()
        => AnomalyRules.Detect(Reading(soh: 70m), Threshold())
            .Should().ContainSingle(a => a.Type == AnomalyTypeEnum.SohDegradation && a.Severity == AlertSeverityEnum.Critical);

    [Fact]
    public void SohDegradation_NotTriggered_WhenSohNull()
        => AnomalyRules.Detect(Reading(soh: null), Threshold())
            .Should().NotContain(a => a.Type == AnomalyTypeEnum.SohDegradation);

    [Fact]
    public void MultipleAnomalies_DetectedSimultaneously()
    {
        var anomalies = AnomalyRules.Detect(Reading(voltage: 15m, temp: 60m, soc: 5m), Threshold());
        anomalies.Select(a => a.Type).Should().BeEquivalentTo(new[]
        {
            AnomalyTypeEnum.Overheat,
            AnomalyTypeEnum.Overvoltage,
            AnomalyTypeEnum.LowSoc
        });
    }

    [Fact]
    public void DetectOffline_Null_WhenLastReadingMissing()
        => AnomalyRules.DetectOffline(new BatteryAsset { LastSensorReadingAt = null }, TimeSpan.FromMinutes(10), DateTime.UtcNow)
            .Should().BeNull();

    [Fact]
    public void DetectOffline_Warning_WhenStale()
    {
        var now = DateTime.UtcNow;
        var asset = new BatteryAsset { LastSensorReadingAt = now.AddMinutes(-15) };
        var result = AnomalyRules.DetectOffline(asset, TimeSpan.FromMinutes(10), now);
        result.Should().NotBeNull();
        result!.Type.Should().Be(AnomalyTypeEnum.DeviceOffline);
        result.Severity.Should().Be(AlertSeverityEnum.Warning);
    }

    [Fact]
    public void DetectOffline_Null_WhenRecent()
    {
        var now = DateTime.UtcNow;
        var asset = new BatteryAsset { LastSensorReadingAt = now.AddMinutes(-5) };
        AnomalyRules.DetectOffline(asset, TimeSpan.FromMinutes(10), now).Should().BeNull();
    }
}
