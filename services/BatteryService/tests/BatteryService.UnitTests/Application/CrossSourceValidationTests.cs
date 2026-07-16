using BatteryService.Application.Anomaly;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Services;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.UnitTests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace BatteryService.UnitTests.Application;

/// <summary>
/// Sprint Bonus NS-09 (#653, N5) — skip so nhiệt độ cho nguồn <c>redundant</c> (INA226 temp=0)
/// + CSVS chỉ ghép cặp Bms ↔ IotGateway (không ghép External).
/// </summary>
public class CrossSourceValidationTests
{
    private static readonly Guid AssetId = Guid.NewGuid();

    private static SensorReading Reading(
        SensorReadingSourceTypeEnum sourceType,
        decimal voltage = 12m,
        decimal temperature = 25m,
        string? sensorSourceCode = null,
        int secondsAgo = 10) => new()
        {
            Time = DateTime.UtcNow.AddSeconds(-secondsAgo),
            BatteryAssetId = AssetId,
            Voltage = voltage,
            Current = 1m,
            Temperature = temperature,
            SocPercent = 50m,
            SourceType = sourceType,
            SensorSourceCode = sensorSourceCode
        };

    private static BatteryAsset MakeAsset() => new()
    {
        Id = AssetId,
        SerialNumber = "B-1",
        BatteryTypeId = Guid.NewGuid(),
        CustomerId = Guid.NewGuid(),
        InstallDate = DateTime.UtcNow,
        Status = BatteryStatusEnum.Active,
        CreatedAt = DateTime.UtcNow
    };

    private static CrossSourceValidationService Sut(MockUnitOfWorkBuilder builder) => new(
        builder.Build(),
        new Mock<IIotMetricsRecorder>().Object,
        NullLogger<CrossSourceValidationService>.Instance);

    // ── AnomalyRules.DetectSensorMismatch ──────────────────────────────────────

    [Fact]
    public void DetectSensorMismatch_RedundantTempZero_SkipsTempComparison()
    {
        var bms = Reading(SensorReadingSourceTypeEnum.Bms, temperature: 25m, sensorSourceCode: "primary");
        var iot = Reading(SensorReadingSourceTypeEnum.IotGateway, temperature: 0m, sensorSourceCode: "redundant");

        AnomalyRules.DetectSensorMismatch(bms, iot).Should().BeNull(
            "redundant không đo nhiệt — ΔT=25°C là giả, không phải mismatch");
    }

    [Fact]
    public void DetectSensorMismatch_RedundantVoltageDelta_StillDetectsVoltagePath()
    {
        var bms = Reading(SensorReadingSourceTypeEnum.Bms, voltage: 12.6m, temperature: 25m, sensorSourceCode: "primary");
        var iot = Reading(SensorReadingSourceTypeEnum.IotGateway, voltage: 12.0m, temperature: 0m, sensorSourceCode: "redundant");

        var result = AnomalyRules.DetectSensorMismatch(bms, iot);

        result.Should().NotBeNull("ΔV=0.6V > 0.5V — đường so sánh điện áp vẫn phải sống");
        result!.Type.Should().Be(AnomalyTypeEnum.SensorMismatch);
        result.Unit.Should().Be("V");
    }

    [Fact]
    public void DetectSensorMismatch_BothMeasureTemp_TempDeltaDetected()
    {
        var bms = Reading(SensorReadingSourceTypeEnum.Bms, temperature: 32m, sensorSourceCode: "primary");
        var iot = Reading(SensorReadingSourceTypeEnum.IotGateway, temperature: 25m, sensorSourceCode: "external-temp");

        var result = AnomalyRules.DetectSensorMismatch(bms, iot);

        result.Should().NotBeNull("ΔT=7°C > 5°C giữa 2 nguồn cùng đo nhiệt");
        result!.Unit.Should().Be("°C");
    }

    // ── CrossSourceValidationService ───────────────────────────────────────────

    [Fact]
    public async Task Scan_BmsVsRedundant_TempZero_NoMismatchAlert()
    {
        var builder = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(MakeAsset())
            .WithSensorReadings(
                Reading(SensorReadingSourceTypeEnum.Bms, temperature: 25m, sensorSourceCode: "primary"),
                Reading(SensorReadingSourceTypeEnum.IotGateway, temperature: 0m, sensorSourceCode: "redundant"));

        var created = await Sut(builder).ScanRecentReadingsAsync(DateTime.UtcNow.AddMinutes(-1));

        created.Should().Be(0);
        builder.Alerts.Verify(r => r.AddAsync(It.IsAny<Alert>()), Times.Never);
    }

    [Fact]
    public async Task Scan_BmsVsRedundant_VoltageDelta_CreatesMismatchAlert()
    {
        var builder = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(MakeAsset())
            .WithSensorReadings(
                Reading(SensorReadingSourceTypeEnum.Bms, voltage: 12.6m, temperature: 25m, sensorSourceCode: "primary"),
                Reading(SensorReadingSourceTypeEnum.IotGateway, voltage: 12.0m, temperature: 0m, sensorSourceCode: "redundant"));

        var created = await Sut(builder).ScanRecentReadingsAsync(DateTime.UtcNow.AddMinutes(-1));

        created.Should().BeGreaterThan(0, "ΔV=0.6V > 0.5V — đường V vẫn phải phát hiện");
        builder.Alerts.Verify(r => r.AddAsync(It.Is<Alert>(a =>
            a.AnomalyType == AnomalyTypeEnum.SensorMismatch)), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Scan_IotGatewayVsExternal_NotPaired_NoAlert()
    {
        // INA226 (IotGateway, temp=0) vs DS18B20 (External, temp thật) từng bị ghép nhầm
        // → ΔT=25°C mismatch giả. Sau N5: External không tham gia ghép cặp.
        var builder = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(MakeAsset())
            .WithSensorReadings(
                Reading(SensorReadingSourceTypeEnum.IotGateway, temperature: 0m, sensorSourceCode: "redundant"),
                Reading(SensorReadingSourceTypeEnum.External, temperature: 25m, sensorSourceCode: "external-temp"));

        var created = await Sut(builder).ScanRecentReadingsAsync(DateTime.UtcNow.AddMinutes(-1));

        created.Should().Be(0);
        builder.Alerts.Verify(r => r.AddAsync(It.IsAny<Alert>()), Times.Never);
    }

    [Fact]
    public async Task Scan_BmsVsIotGateway_TempDelta_CreatesMismatchAlert()
    {
        var builder = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(MakeAsset())
            .WithSensorReadings(
                Reading(SensorReadingSourceTypeEnum.Bms, temperature: 32m, sensorSourceCode: "primary"),
                Reading(SensorReadingSourceTypeEnum.IotGateway, temperature: 25m, sensorSourceCode: "external-temp"));

        var created = await Sut(builder).ScanRecentReadingsAsync(DateTime.UtcNow.AddMinutes(-1));

        created.Should().BeGreaterThan(0, "ΔT=7°C > 5°C giữa 2 nguồn cùng đo nhiệt");
    }
}
