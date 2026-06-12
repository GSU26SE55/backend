using BatteryService.Application.Anomaly;
using BatteryService.Application.Services;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.UnitTests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace BatteryService.UnitTests.Application;

/// <summary>
/// Sprint 5B B1 (#152) — NoiseSuppression frequency-based logic.
/// </summary>
public class NoiseSuppressionTests
{
    private static readonly Guid AssetId = Guid.NewGuid();
    private static readonly Guid CustomerId = Guid.NewGuid();
    private static readonly Guid BatteryTypeId = Guid.NewGuid();

    private static IOptions<AnomalyEngineOptions> Opts() =>
        Options.Create(new AnomalyEngineOptions { DedupWindowMinutes = 30 });

    private static BatteryAsset MakeAsset() => new()
    {
        Id = AssetId,
        SerialNumber = "B-1",
        BatteryTypeId = BatteryTypeId,
        CustomerId = CustomerId,
        InstallDate = DateTime.UtcNow,
        Status = BatteryStatusEnum.Active,
        CreatedAt = DateTime.UtcNow
    };

    private static ThresholdConfig MakeThreshold(bool noise = false, int count = 3, int hours = 1)
        => new()
        {
            Id = Guid.NewGuid(),
            BatteryTypeId = BatteryTypeId,
            VoltageMin = 10,
            VoltageMax = 14,
            TemperatureMin = -10,
            TemperatureMax = 50,
            SocWarningThreshold = 20,
            SocCriticalThreshold = 10,
            IsActive = true,
            EffectiveFromUtc = DateTime.UtcNow,
            NoiseSuppressionEnabled = noise,
            NoiseSuppressionCount = count,
            NoiseSuppressionWindowHours = hours
        };

    private static SensorReading MakeReading(decimal voltage)
        => new()
        {
            Time = DateTime.UtcNow,
            BatteryAssetId = AssetId,
            Voltage = voltage,
            Current = 1m,
            Temperature = 25m,
            SocPercent = 50m
        };

    [Fact]
    public async Task NoiseSuppression_Disabled_ShouldAlwaysRaiseAlert()
    {
        var builder = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(MakeAsset())
            .WithThresholdConfigs(MakeThreshold(noise: false))
            .WithSensorReadings(MakeReading(voltage: 9m)); // < VoltageMin → Undervoltage Critical

        var sut = new AnomalyDetectionService(builder.Build(), Opts());
        var result = await sut.ScanRecentReadingsAsync(TimeSpan.FromMinutes(5));

        result.AlertsCreated.Should().BeGreaterOrEqualTo(1);
        result.AlertsSuppressed.Should().Be(0);
    }

    [Fact]
    public async Task NoiseSuppression_Enabled_FirstBreach_ShouldSuppress()
    {
        var builder = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(MakeAsset())
            .WithThresholdConfigs(MakeThreshold(noise: true, count: 3, hours: 1))
            .WithSensorReadings(MakeReading(voltage: 9m));

        var sut = new AnomalyDetectionService(builder.Build(), Opts());
        var result = await sut.ScanRecentReadingsAsync(TimeSpan.FromMinutes(5));

        result.AlertsSuppressed.Should().Be(1);
        result.AlertsCreated.Should().Be(0);
    }

    [Fact]
    public async Task NoiseSuppression_ThirdBreachInWindow_ShouldRaiseAlert()
    {
        var now = DateTime.UtcNow;
        var prior = new[]
        {
            new NoiseBreachEvent { Time = now.AddMinutes(-10), BatteryAssetId = AssetId, AnomalyType = AnomalyTypeEnum.Undervoltage, ThresholdValue = 10, ActualValue = 9, Unit = "V" },
            new NoiseBreachEvent { Time = now.AddMinutes(-5),  BatteryAssetId = AssetId, AnomalyType = AnomalyTypeEnum.Undervoltage, ThresholdValue = 10, ActualValue = 9, Unit = "V" }
        };

        var builder = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(MakeAsset())
            .WithThresholdConfigs(MakeThreshold(noise: true, count: 3, hours: 1))
            .WithSensorReadings(MakeReading(voltage: 9m))
            .WithNoiseBreachEvents(prior);

        var sut = new AnomalyDetectionService(builder.Build(), Opts());
        var result = await sut.ScanRecentReadingsAsync(TimeSpan.FromMinutes(15));

        // Đây là breach lần 3 → raise alert.
        result.AlertsCreated.Should().Be(1);
        result.AlertsSuppressed.Should().Be(0);
    }

    [Fact]
    public async Task NoiseSuppression_BypassCriticalOverheat_ShouldAlwaysRaise()
    {
        var reading = new SensorReading
        {
            Time = DateTime.UtcNow,
            BatteryAssetId = AssetId,
            Voltage = 12m,
            Current = 1m,
            Temperature = 70m,
            SocPercent = 50m
        };

        var builder = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(MakeAsset())
            .WithThresholdConfigs(MakeThreshold(noise: true, count: 5, hours: 1))
            .WithSensorReadings(reading);

        var sut = new AnomalyDetectionService(builder.Build(), Opts());
        var result = await sut.ScanRecentReadingsAsync(TimeSpan.FromMinutes(5));

        // Critical Overheat bypass — fire alert ngay không suppression.
        result.AlertsCreated.Should().Be(1);
        result.AlertsSuppressed.Should().Be(0);
    }

    [Fact]
    public async Task NoiseSuppression_CountEqualOne_ShouldNotSuppress()
    {
        var builder = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(MakeAsset())
            .WithThresholdConfigs(MakeThreshold(noise: true, count: 1, hours: 1))
            .WithSensorReadings(MakeReading(voltage: 9m));

        var sut = new AnomalyDetectionService(builder.Build(), Opts());
        var result = await sut.ScanRecentReadingsAsync(TimeSpan.FromMinutes(5));

        result.AlertsCreated.Should().Be(1);
        result.AlertsSuppressed.Should().Be(0);
    }

    [Fact]
    public async Task NoiseSuppression_BreachOutsideWindow_ShouldStillSuppress()
    {
        var now = DateTime.UtcNow;
        var stale = new[]
        {
            new NoiseBreachEvent { Time = now.AddHours(-3), BatteryAssetId = AssetId, AnomalyType = AnomalyTypeEnum.Undervoltage, ThresholdValue = 10, ActualValue = 9, Unit = "V" },
            new NoiseBreachEvent { Time = now.AddHours(-2), BatteryAssetId = AssetId, AnomalyType = AnomalyTypeEnum.Undervoltage, ThresholdValue = 10, ActualValue = 9, Unit = "V" }
        };

        var builder = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(MakeAsset())
            .WithThresholdConfigs(MakeThreshold(noise: true, count: 3, hours: 1))
            .WithSensorReadings(MakeReading(voltage: 9m))
            .WithNoiseBreachEvents(stale);

        var sut = new AnomalyDetectionService(builder.Build(), Opts());
        var result = await sut.ScanRecentReadingsAsync(TimeSpan.FromMinutes(5));

        // Stale breaches (>1h ago) đếm không vào window → vẫn suppress.
        result.AlertsSuppressed.Should().Be(1);
        result.AlertsCreated.Should().Be(0);
    }
}
