using BatteryService.Application.Anomaly;
using BatteryService.Application.Services;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.UnitTests.Helpers;
using Microsoft.Extensions.Options;

namespace BatteryService.UnitTests.Application;

public class AnomalyDetectionServiceTests
{
    private static readonly Guid AssetId = Guid.NewGuid();
    private static readonly Guid CustomerId = Guid.NewGuid();
    private static readonly Guid BatteryTypeId = Guid.NewGuid();

    private static IOptions<AnomalyEngineOptions> Opts() =>
        Options.Create(new AnomalyEngineOptions { DedupWindowMinutes = 30 });

    private static BatteryAsset MakeAsset() => new()
    {
        Id = AssetId,
        SerialNumber = "BAT-001",
        BatteryTypeId = BatteryTypeId,
        CustomerId = CustomerId,
        InstallDate = DateTime.UtcNow.AddDays(-30),
        Status = BatteryStatusEnum.Active,
        CreatedAt = DateTime.UtcNow
    };

    private static ThresholdConfig MakeThreshold() => new()
    {
        Id = Guid.NewGuid(),
        BatteryTypeId = BatteryTypeId,
        VoltageMin = 10,
        VoltageMax = 14,
        TemperatureMin = -10,
        TemperatureMax = 50,
        SocWarningThreshold = 20,
        SocCriticalThreshold = 10,
        SohWarningThreshold = 85,
        SohCriticalThreshold = 75,
        // Tắt noise suppression cho các test cũ (chúng kiểm tra hành vi detect, không phải suppression).
        NoiseSuppressionEnabled = false,
        IsActive = true,
        EffectiveFromUtc = DateTime.UtcNow
    };

    private static SensorReading MakeReading(decimal temp = 25, decimal voltage = 12, decimal soc = 50, decimal? soh = null) => new()
    {
        Time = DateTime.UtcNow.AddSeconds(-10),
        BatteryAssetId = AssetId,
        Voltage = voltage,
        Current = 0,
        Temperature = temp,
        SocPercent = soc,
        SohPercent = soh
    };

    [Fact]
    public async Task Scan_NoReadings_ReturnsEmptyResult()
    {
        var b = new MockUnitOfWorkBuilder();
        var svc = new AnomalyDetectionService(b.Build(), Opts());

        var result = await svc.ScanRecentReadingsAsync(TimeSpan.FromMinutes(1), CancellationToken.None);

        result.ReadingsScanned.Should().Be(0);
        result.AlertsCreated.Should().Be(0);
    }

    [Fact]
    public async Task Scan_NormalReading_NoAlerts()
    {
        var b = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(MakeAsset())
            .WithThresholdConfigs(MakeThreshold())
            .WithSensorReadings(MakeReading());
        var svc = new AnomalyDetectionService(b.Build(), Opts());

        var result = await svc.ScanRecentReadingsAsync(TimeSpan.FromMinutes(1), CancellationToken.None);

        result.ReadingsScanned.Should().Be(1);
        result.AlertsCreated.Should().Be(0);
        result.AlertsMerged.Should().Be(0);
        b.Alerts.Verify(r => r.AddAsync(It.IsAny<Alert>()), Times.Never);
    }

    [Fact]
    public async Task Scan_CriticalReading_CreatesAlertAndOutbox()
    {
        var b = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(MakeAsset())
            .WithThresholdConfigs(MakeThreshold())
            .WithSensorReadings(MakeReading(temp: 60m));
        var svc = new AnomalyDetectionService(b.Build(), Opts());

        var result = await svc.ScanRecentReadingsAsync(TimeSpan.FromMinutes(1), CancellationToken.None);

        result.AlertsCreated.Should().Be(1);
        result.OutboxEventsQueued.Should().Be(1);
        b.Alerts.Verify(r => r.AddAsync(It.Is<Alert>(a =>
            a.AnomalyType == AnomalyTypeEnum.Overheat
            && a.Severity == AlertSeverityEnum.Critical
            && a.Status == AlertStatusEnum.Open)), Times.Once);
        b.OutboxMessages.Verify(r => r.AddAsync(It.Is<OutboxMessage>(m =>
            m.Type == "BatteryAnomalyDetectedEvent"
            && m.Payload.Contains("BAT-001"))), Times.Once);
    }

    [Fact]
    public async Task Scan_WarningReading_CreatesAlert_NoOutbox()
    {
        var b = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(MakeAsset())
            .WithThresholdConfigs(MakeThreshold())
            .WithSensorReadings(MakeReading(temp: 53m));
        var svc = new AnomalyDetectionService(b.Build(), Opts());

        var result = await svc.ScanRecentReadingsAsync(TimeSpan.FromMinutes(1), CancellationToken.None);

        result.AlertsCreated.Should().Be(1);
        result.OutboxEventsQueued.Should().Be(0);
        b.Alerts.Verify(r => r.AddAsync(It.Is<Alert>(a => a.Severity == AlertSeverityEnum.Warning)), Times.Once);
        b.OutboxMessages.Verify(r => r.AddAsync(It.IsAny<OutboxMessage>()), Times.Never);
    }

    [Fact]
    public async Task Scan_WithinDedupWindow_MergesAlert_NoSecondOutbox()
    {
        var existing = new Alert
        {
            Id = Guid.NewGuid(),
            BatteryAssetId = AssetId,
            AnomalyType = AnomalyTypeEnum.Overheat,
            Severity = AlertSeverityEnum.Critical,
            ThresholdValue = 50,
            ActualValue = 60,
            Unit = "°C",
            DetectedAt = DateTime.UtcNow.AddMinutes(-5),
            Status = AlertStatusEnum.Open,
            DedupWindowEndUtc = DateTime.UtcNow.AddMinutes(25),
            CreatedAt = DateTime.UtcNow.AddMinutes(-5)
        };
        var b = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(MakeAsset())
            .WithThresholdConfigs(MakeThreshold())
            .WithSensorReadings(MakeReading(temp: 60m))
            .WithAlerts(existing);
        var svc = new AnomalyDetectionService(b.Build(), Opts());

        var result = await svc.ScanRecentReadingsAsync(TimeSpan.FromMinutes(1), CancellationToken.None);

        result.AlertsCreated.Should().Be(0);
        result.AlertsMerged.Should().Be(1);
        b.Alerts.Verify(r => r.AddAsync(It.Is<Alert>(a =>
            a.Status == AlertStatusEnum.Merged
            && a.MergedIntoAlertId == existing.Id)), Times.Once);
        b.OutboxMessages.Verify(r => r.AddAsync(It.IsAny<OutboxMessage>()), Times.Never);
    }

    [Fact]
    public async Task Scan_SohDegradation_TriggersCriticalAlert()
    {
        var b = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(MakeAsset())
            .WithThresholdConfigs(MakeThreshold())
            .WithSensorReadings(MakeReading(soh: 70m));
        var svc = new AnomalyDetectionService(b.Build(), Opts());

        var result = await svc.ScanRecentReadingsAsync(TimeSpan.FromMinutes(1), CancellationToken.None);

        result.AlertsCreated.Should().Be(1);
        b.Alerts.Verify(r => r.AddAsync(It.Is<Alert>(a =>
            a.AnomalyType == AnomalyTypeEnum.SohDegradation
            && a.Severity == AlertSeverityEnum.Critical)), Times.Once);
    }

    [Fact]
    public async Task Scan_AssetNotFound_SkipsReading()
    {
        var b = new MockUnitOfWorkBuilder()
            .WithThresholdConfigs(MakeThreshold())
            .WithSensorReadings(MakeReading(temp: 60m));
        var svc = new AnomalyDetectionService(b.Build(), Opts());

        var result = await svc.ScanRecentReadingsAsync(TimeSpan.FromMinutes(1), CancellationToken.None);

        result.ReadingsScanned.Should().Be(1);
        result.AlertsCreated.Should().Be(0);
    }

    [Fact]
    public async Task Scan_ThresholdNotFound_SkipsReading()
    {
        var b = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(MakeAsset())
            .WithSensorReadings(MakeReading(temp: 60m));
        var svc = new AnomalyDetectionService(b.Build(), Opts());

        var result = await svc.ScanRecentReadingsAsync(TimeSpan.FromMinutes(1), CancellationToken.None);

        result.ReadingsScanned.Should().Be(1);
        result.AlertsCreated.Should().Be(0);
    }
}
