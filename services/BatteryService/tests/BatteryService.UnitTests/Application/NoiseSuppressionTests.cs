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
            VoltageMin = 14,
            VoltageMax = 15,
            TemperatureMin = 45,
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
            .WithSensorReadings(MakeReading(voltage: 15.5m)); // > VoltageMax (mốc Critical) → Overvoltage Critical

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
            .WithSensorReadings(MakeReading(voltage: 15.5m));

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
            new NoiseBreachEvent { Time = now.AddMinutes(-10), BatteryAssetId = AssetId, AnomalyType = AnomalyTypeEnum.Overvoltage, ThresholdValue = 15, ActualValue = 15.5m, Unit = "V" },
            new NoiseBreachEvent { Time = now.AddMinutes(-5),  BatteryAssetId = AssetId, AnomalyType = AnomalyTypeEnum.Overvoltage, ThresholdValue = 15, ActualValue = 15.5m, Unit = "V" }
        };

        var builder = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(MakeAsset())
            .WithThresholdConfigs(MakeThreshold(noise: true, count: 3, hours: 1))
            .WithSensorReadings(MakeReading(voltage: 15.5m))
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
            .WithSensorReadings(MakeReading(voltage: 15.5m));

        var sut = new AnomalyDetectionService(builder.Build(), Opts());
        var result = await sut.ScanRecentReadingsAsync(TimeSpan.FromMinutes(5));

        result.AlertsCreated.Should().Be(1);
        result.AlertsSuppressed.Should().Be(0);
    }

    [Fact]
    public async Task NoiseSuppression_OnlySuppressedTick_PersistsBreachEvents()
    {
        // Sprint Bonus NS-07 (#651, N1) — tick chỉ toàn suppress vẫn phải SaveChanges
        // để NoiseBreachEvent pending không bị vứt cùng DbContext scope.
        var builder = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(MakeAsset())
            .WithThresholdConfigs(MakeThreshold(noise: true, count: 5, hours: 1))
            .WithSensorReadings(MakeReading(voltage: 15.5m));

        var sut = new AnomalyDetectionService(builder.Build(), Opts());
        var result = await sut.ScanRecentReadingsAsync(TimeSpan.FromMinutes(5));

        result.AlertsSuppressed.Should().Be(1);
        result.AlertsCreated.Should().Be(0);
        builder.NoiseBreachEvents.Verify(r => r.AddAsync(It.IsAny<NoiseBreachEvent>()), Times.Once);
        builder.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NoiseSuppression_FiveConsecutiveTicks_FifthTickRaisesAlert()
    {
        // Sprint Bonus NS-07 (#651, N1) — kịch bản tái hiện bug: vi phạm lai rai qua nhiều tick,
        // breach persist dần → tick 5 (count=5) alert phải nổ. Trước fix, breach bị vứt mỗi tick
        // → count mãi = 0 → không bao giờ nổ.
        var builder = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(MakeAsset())
            .WithThresholdConfigs(MakeThreshold(noise: true, count: 5, hours: 1));

        var sut = new AnomalyDetectionService(builder.Build(), Opts());

        for (var tick = 1; tick <= 4; tick++)
        {
            builder.WithSensorReadings(MakeReading(voltage: 15.5m)); // thay reading mới mỗi tick
            var r = await sut.ScanRecentReadingsAsync(TimeSpan.FromMinutes(5));
            r.AlertsSuppressed.Should().Be(1, $"tick {tick} chưa đủ tần suất");
            r.AlertsCreated.Should().Be(0, $"tick {tick} chưa đủ tần suất");
        }

        builder.WithSensorReadings(MakeReading(voltage: 15.5m));
        var fifth = await sut.ScanRecentReadingsAsync(TimeSpan.FromMinutes(5));

        fifth.AlertsCreated.Should().Be(1, "tick 5 đạt ngưỡng NoiseSuppressionCount=5");
        fifth.AlertsSuppressed.Should().Be(0);
        builder.UnitOfWork.Verify(
            u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(5),
            "cả 4 tick suppress lẫn tick 5 tạo alert đều phải save");
    }

    [Fact]
    public async Task NoiseSuppression_BreachRecordedWithReadingTimeAndSourceType()
    {
        // Sprint Bonus NS-10 (#654, N3) — breach ghi theo reading.Time (phục vụ dedup)
        // + copy SourceType từ reading (B9 — phân biệt breach từ BMS hay IoT).
        var reading = MakeReading(voltage: 15.5m);
        reading.SourceType = SensorReadingSourceTypeEnum.Bms;

        var builder = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(MakeAsset())
            .WithThresholdConfigs(MakeThreshold(noise: true, count: 3, hours: 1))
            .WithSensorReadings(reading);

        var sut = new AnomalyDetectionService(builder.Build(), Opts());
        await sut.ScanRecentReadingsAsync(TimeSpan.FromMinutes(5));

        builder.NoiseBreachEvents.Verify(r => r.AddAsync(It.Is<NoiseBreachEvent>(n =>
            n.Time == reading.Time
            && n.SourceType == SensorReadingSourceTypeEnum.Bms
            && n.BatteryAssetId == AssetId)), Times.Once);
    }

    [Fact]
    public async Task NoiseSuppression_SameReadingScannedTwice_RecordsSingleBreach()
    {
        // Sprint Bonus NS-10 (#654, N3) — lookback overlap 2× khiến cùng 1 reading bị scan
        // ở 2 tick liên tiếp → chỉ được 1 breach (dedup theo assetId+anomalyType+reading.Time).
        var reading = MakeReading(voltage: 15.5m);
        var builder = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(MakeAsset())
            .WithThresholdConfigs(MakeThreshold(noise: true, count: 5, hours: 1))
            .WithSensorReadings(reading);

        var sut = new AnomalyDetectionService(builder.Build(), Opts());
        var tick1 = await sut.ScanRecentReadingsAsync(TimeSpan.FromMinutes(5));
        var tick2 = await sut.ScanRecentReadingsAsync(TimeSpan.FromMinutes(5)); // reading cũ còn trong lookback

        tick1.AlertsSuppressed.Should().Be(1);
        tick2.AlertsSuppressed.Should().Be(1);
        builder.NoiseBreachEvents.Verify(r => r.AddAsync(It.IsAny<NoiseBreachEvent>()), Times.Once,
            "cùng 1 reading không được đếm thành 2 breach");
    }

    [Fact]
    public async Task NoiseSuppression_AlertRaised_PromotesBreachChain()
    {
        // Sprint Bonus NS-10 (#654, N2) — alert nổ từ chuỗi breach → mọi breach trong window
        // được gán PromotedToAlertId (audit), gồm cả breach pending của tick hiện tại.
        var now = DateTime.UtcNow;
        var prior = new[]
        {
            new NoiseBreachEvent { Time = now.AddMinutes(-10), BatteryAssetId = AssetId, AnomalyType = AnomalyTypeEnum.Overvoltage, ThresholdValue = 15, ActualValue = 15.5m, Unit = "V" },
            new NoiseBreachEvent { Time = now.AddMinutes(-5),  BatteryAssetId = AssetId, AnomalyType = AnomalyTypeEnum.Overvoltage, ThresholdValue = 15, ActualValue = 15.5m, Unit = "V" }
        };

        var builder = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(MakeAsset())
            .WithThresholdConfigs(MakeThreshold(noise: true, count: 3, hours: 1))
            .WithSensorReadings(MakeReading(voltage: 15.5m))
            .WithNoiseBreachEvents(prior);

        var sut = new AnomalyDetectionService(builder.Build(), Opts());
        var result = await sut.ScanRecentReadingsAsync(TimeSpan.FromMinutes(15));

        result.AlertsCreated.Should().Be(1);
        prior[0].PromotedToAlertId.Should().NotBeNull("breach cũ trong window phải được link vào alert");
        prior[1].PromotedToAlertId.Should().NotBeNull();
        prior[0].PromotedToAlertId.Should().Be(prior[1].PromotedToAlertId!.Value, "cả chuỗi link cùng 1 alert");
        builder.NoiseBreachEvents.Verify(r => r.AddAsync(It.Is<NoiseBreachEvent>(n =>
            n.PromotedToAlertId != null)), Times.Once, "breach pending của tick nổ alert cũng phải được promote");
    }

    [Fact]
    public async Task NoiseSuppression_Suppressed_DoesNotPromoteBreaches()
    {
        var prior = new NoiseBreachEvent
        {
            Time = DateTime.UtcNow.AddMinutes(-10),
            BatteryAssetId = AssetId,
            AnomalyType = AnomalyTypeEnum.Overvoltage,
            ThresholdValue = 10,
            ActualValue = 9,
            Unit = "V"
        };
        var builder = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(MakeAsset())
            .WithThresholdConfigs(MakeThreshold(noise: true, count: 5, hours: 1))
            .WithSensorReadings(MakeReading(voltage: 15.5m))
            .WithNoiseBreachEvents(prior);

        var sut = new AnomalyDetectionService(builder.Build(), Opts());
        var result = await sut.ScanRecentReadingsAsync(TimeSpan.FromMinutes(15));

        result.AlertsSuppressed.Should().Be(1);
        prior.PromotedToAlertId.Should().BeNull("chưa nổ alert thì chuỗi breach chưa được promote");
    }

    [Fact]
    public async Task NoiseSuppression_BreachOutsideWindow_ShouldStillSuppress()
    {
        var now = DateTime.UtcNow;
        var stale = new[]
        {
            new NoiseBreachEvent { Time = now.AddHours(-3), BatteryAssetId = AssetId, AnomalyType = AnomalyTypeEnum.Overvoltage, ThresholdValue = 15, ActualValue = 15.5m, Unit = "V" },
            new NoiseBreachEvent { Time = now.AddHours(-2), BatteryAssetId = AssetId, AnomalyType = AnomalyTypeEnum.Overvoltage, ThresholdValue = 15, ActualValue = 15.5m, Unit = "V" }
        };

        var builder = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(MakeAsset())
            .WithThresholdConfigs(MakeThreshold(noise: true, count: 3, hours: 1))
            .WithSensorReadings(MakeReading(voltage: 15.5m))
            .WithNoiseBreachEvents(stale);

        var sut = new AnomalyDetectionService(builder.Build(), Opts());
        var result = await sut.ScanRecentReadingsAsync(TimeSpan.FromMinutes(5));

        // Stale breaches (>1h ago) đếm không vào window → vẫn suppress.
        result.AlertsSuppressed.Should().Be(1);
        result.AlertsCreated.Should().Be(0);
    }
}
