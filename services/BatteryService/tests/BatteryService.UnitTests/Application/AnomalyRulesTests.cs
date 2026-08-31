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
        // Min = mốc Warning, Max = mốc Critical — thang MỘT CHIỀU, không phải dải an toàn.
        VoltageMin = 14m,
        VoltageMax = 15m,
        TemperatureMin = 45m,
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

    // Cả hai mốc đều là số Admin đặt: trên Min là Warning, trên Max là Critical. Không có hằng
    // số nào suy ra ở giữa — đây là bộ test vỡ nếu ai đưa `±5` chôn trong code quay lại.
    [Fact]
    public void Overheat_None_BelowWarning()
        => AnomalyRules.Detect(Reading(temp: 44m), Threshold())
            .Should().NotContain(a => a.Type == AnomalyTypeEnum.Overheat);

    [Fact]
    public void Overheat_Warning_AboveWarningThreshold()
        => AnomalyRules.Detect(Reading(temp: 46m), Threshold())
            .Should().ContainSingle(a => a.Type == AnomalyTypeEnum.Overheat && a.Severity == AlertSeverityEnum.Warning);

    [Fact]
    public void Overheat_Critical_AboveCriticalThreshold()
        => AnomalyRules.Detect(Reading(temp: 51m), Threshold())
            .Should().ContainSingle(a => a.Type == AnomalyTypeEnum.Overheat && a.Severity == AlertSeverityEnum.Critical);

    // Đúng BẰNG mốc là ĐÃ vi phạm. Admin đặt 45 nghĩa là "từ 45 trở lên báo cho tôi"; số đo và
    // ngưỡng đều chỉ có 2 chữ số thập phân nên 45.00 là giá trị chạm tới được thật.
    [Fact]
    public void Overheat_Warning_ExactlyAtWarningThreshold()
        => AnomalyRules.Detect(Reading(temp: 45m), Threshold())
            .Should().ContainSingle(a => a.Type == AnomalyTypeEnum.Overheat && a.Severity == AlertSeverityEnum.Warning);

    [Fact]
    public void Overheat_Critical_ExactlyAtCriticalThreshold()
        => AnomalyRules.Detect(Reading(temp: 50m), Threshold())
            .Should().ContainSingle(a => a.Type == AnomalyTypeEnum.Overheat && a.Severity == AlertSeverityEnum.Critical);

    // Vượt mốc Critical chỉ ra MỘT alert, không chồng thêm một Warning cho cùng số đo.
    [Fact]
    public void Overheat_Critical_DoesNotAlsoRaiseWarning()
        => AnomalyRules.Detect(Reading(temp: 51m), Threshold())
            .Where(a => a.Type == AnomalyTypeEnum.Overheat)
            .Should().HaveCount(1);

    // Phía thấp KHÔNG còn rule: thang một chiều nên lạnh/sụt áp không diễn đạt được nữa.
    [Fact]
    public void ColdReading_RaisesNothing()
        => AnomalyRules.Detect(Reading(temp: -30m, voltage: 5m), Threshold())
            .Should().BeEmpty();

    // ===== Ca thật trên máy: BAT-24V-JK-V1 (LiFePO4 24V 30Ah), gateway online =====
    //
    // Khoá đúng bộ số Admin đang đặt trên màn "Configure alert thresholds" và số đo realtime đọc
    // được cùng lúc, để BE và màu trên FE không bao giờ nói hai chuyện khác nhau.
    private static ThresholdConfig DemoThreshold() => new()
    {
        Id = Guid.NewGuid(),
        BatteryTypeId = Guid.NewGuid(),
        VoltageMin = 25m,      // Warning
        VoltageMax = 27m,      // Critical
        TemperatureMin = 30m,  // Warning
        TemperatureMax = 32m,  // Critical
        SocWarningThreshold = 91m,
        SocCriticalThreshold = 89m,
        CurrentMaxCharge = 2m,
        CurrentMaxDischarge = 3m,
        SohWarningThreshold = 80m,
        SohCriticalThreshold = 75m,
        EffectiveFromUtc = DateTime.UtcNow,
        IsActive = true
    };

    [Fact]
    public void DemoAsset_LiveReading_RaisesWarningOnVoltageTemperatureAndSoc()
    {
        // Số đo realtime: 26.65 V · 0.00 A · 31.60 °C · 91.00 %
        var result = AnomalyRules.Detect(
            Reading(voltage: 26.65m, current: 0m, temp: 31.6m, soc: 91m), DemoThreshold());

        result.Should().ContainSingle(a =>
            a.Type == AnomalyTypeEnum.Overvoltage && a.Severity == AlertSeverityEnum.Warning,
            "26.65 V đã qua mốc Warning 25 nhưng chưa tới Critical 27");
        result.Should().ContainSingle(a =>
            a.Type == AnomalyTypeEnum.Overheat && a.Severity == AlertSeverityEnum.Warning,
            "31.60 °C đã qua mốc Warning 30 nhưng chưa tới Critical 32");
        result.Should().ContainSingle(a =>
            a.Type == AnomalyTypeEnum.LowSoc && a.Severity == AlertSeverityEnum.Warning,
            "SOC 91.00 ĐÚNG BẰNG mốc Warning 91 — so sánh bao gồm mốc nên đã tính là vi phạm");

        // 0 A nằm trong cả trần sạc 2 A lẫn trần xả 3 A.
        result.Should().NotContain(a =>
            a.Type == AnomalyTypeEnum.AbnormalCharging || a.Type == AnomalyTypeEnum.RapidDischarge);

        // Toàn Warning ⇒ chỉ notify, KHÔNG đẻ ticket. Ticket chỉ sinh từ Critical.
        result.Should().OnlyContain(a => a.Severity == AlertSeverityEnum.Warning);
    }

    [Fact]
    public void DemoAsset_ReachingCriticalThresholds_RaisesCritical()
    {
        var result = AnomalyRules.Detect(
            Reading(voltage: 27m, current: 2m, temp: 32m, soc: 89m), DemoThreshold());

        result.Should().ContainSingle(a =>
            a.Type == AnomalyTypeEnum.Overvoltage && a.Severity == AlertSeverityEnum.Critical);
        result.Should().ContainSingle(a =>
            a.Type == AnomalyTypeEnum.Overheat && a.Severity == AlertSeverityEnum.Critical);
        result.Should().ContainSingle(a =>
            a.Type == AnomalyTypeEnum.LowSoc && a.Severity == AlertSeverityEnum.Critical);
        result.Should().ContainSingle(a =>
            a.Type == AnomalyTypeEnum.AbnormalCharging, "dòng sạc 2 A đúng bằng trần 2 A");
    }

    [Fact]
    public void DemoAsset_ReadingBelowEveryThreshold_RaisesNothing()
        => AnomalyRules.Detect(
                Reading(voltage: 24.99m, current: 1m, temp: 29.99m, soc: 91.01m), DemoThreshold())
            .Should().BeEmpty();

    [Fact]
    public void Overvoltage_Warning_AboveWarningThreshold()
        => AnomalyRules.Detect(Reading(voltage: 14.5m), Threshold())
            .Should().ContainSingle(a => a.Type == AnomalyTypeEnum.Overvoltage && a.Severity == AlertSeverityEnum.Warning);

    [Fact]
    public void Overvoltage_Critical_AboveCriticalThreshold()
        => AnomalyRules.Detect(Reading(voltage: 15.5m), Threshold())
            .Should().ContainSingle(a => a.Type == AnomalyTypeEnum.Overvoltage && a.Severity == AlertSeverityEnum.Critical);

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
