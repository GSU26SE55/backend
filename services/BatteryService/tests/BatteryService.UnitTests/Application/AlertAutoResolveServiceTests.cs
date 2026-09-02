using BatteryService.Application.Services;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.UnitTests.Helpers;

namespace BatteryService.UnitTests.Application;

/// <summary>
/// GH-783 — auto-resolve chấm alert còn hợp lệ hay không bằng <c>AnomalyRules.Detect()</c>,
/// tức bộ rule ngưỡng cứng. Alert do AI sinh (SohDegradation) không nằm trong bộ rule đó nên
/// luôn ra "hết anomaly" → bị resolve nhầm, phá invariant "1 asset = 1 alert SOH chưa resolve"
/// mà <c>SohPredictionBackgroundService</c> vừa thiết lập.
/// </summary>
public class AlertAutoResolveServiceTests
{
    private const int LookbackMinutes = 10;

    [Fact]
    public async Task AutoResolve_SohDegradationAlert_IsSkipped()
    {
        var (uow, alert) = Harness(AnomalyTypeEnum.SohDegradation);
        var sut = new AlertAutoResolveService(uow.Build());

        var result = await sut.AutoResolveAsync(LookbackMinutes);

        result.Resolved.Should().Be(0);
        alert.Status.Should().Be(AlertStatusEnum.Open);
        alert.ResolvedAt.Should().BeNull();
    }

    [Fact]
    public async Task AutoResolve_ThresholdAlert_StillResolves()
    {
        // Control: cùng bộ dữ liệu, alert từ rule ngưỡng vẫn được resolve như trước —
        // chứng minh test trên fail vì bộ lọc AnomalyType, không phải vì harness dựng sai.
        var (uow, alert) = Harness(AnomalyTypeEnum.Overheat);
        var sut = new AlertAutoResolveService(uow.Build());

        var result = await sut.AutoResolveAsync(LookbackMinutes);

        result.Resolved.Should().Be(1);
        alert.Status.Should().Be(AlertStatusEnum.Resolved);
    }

    /// <summary>
    /// Known gap (2026-09-02) — SensorMismatch cần so 2 nguồn (BMS vs IoT) trong 1 cửa sổ hẹp,
    /// không tái tạo được từ 1 SensorReading đơn qua AnomalyRules.Detect(), nên bị loại trừ
    /// tường minh khỏi vòng quét. Warning SensorMismatch không có Ticket (chỉ Critical mới
    /// auto-tạo) nên cũng không có đường resolve nào khác — chấp nhận có chủ đích, phải resolve tay.
    /// </summary>
    [Fact]
    public async Task AutoResolve_SensorMismatchAlert_IsSkipped()
    {
        var (uow, alert) = Harness(AnomalyTypeEnum.SensorMismatch);
        var sut = new AlertAutoResolveService(uow.Build());

        var result = await sut.AutoResolveAsync(LookbackMinutes);

        result.Resolved.Should().Be(0);
        alert.Status.Should().Be(AlertStatusEnum.Open);
        alert.ResolvedAt.Should().BeNull();
    }

    /// <summary>
    /// Known gap (2026-09-02) — DeviceOffline chính là "không còn reading mới", nên không có
    /// bằng chứng sensor nào để tự suy luận lại; loại trừ tường minh khỏi vòng quét. Nếu Critical
    /// (có Ticket) thì AlertResolveOnTicketClosedConsumer vẫn resolve được khi ticket Closed —
    /// nhưng Warning DeviceOffline (không có Ticket) vẫn kẹt vĩnh viễn, phải resolve tay.
    /// </summary>
    [Fact]
    public async Task AutoResolve_DeviceOfflineAlert_IsSkipped()
    {
        var (uow, alert) = Harness(AnomalyTypeEnum.DeviceOffline);
        var sut = new AlertAutoResolveService(uow.Build());

        var result = await sut.AutoResolveAsync(LookbackMinutes);

        result.Resolved.Should().Be(0);
        alert.Status.Should().Be(AlertStatusEnum.Open);
        alert.ResolvedAt.Should().BeNull();
    }

    /// <summary>
    /// Env alert (BatteryAssetId null, site-level) trước đây không có đường tự resolve nào —
    /// chỉ Critical mới auto-tạo Ticket nên Warning Env luôn phải resolve tay dù cảm biến đã
    /// về ngưỡng an toàn. Nhánh Ambient mới trong AutoResolveAsync lấp đúng gap này.
    /// </summary>
    [Fact]
    public async Task AutoResolve_AmbientAlert_NoLongerBreachingThreshold_Resolves()
    {
        var (uow, alert) = AmbientHarness(reading => reading.AmbientTemperature = 30m);
        var sut = new AlertAutoResolveService(uow.Build());

        var result = await sut.AutoResolveAsync(LookbackMinutes);

        result.Resolved.Should().Be(1);
        alert.Status.Should().Be(AlertStatusEnum.Resolved);
        alert.ResolvedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task AutoResolve_AmbientAlert_StillBreachingThreshold_IsSkipped()
    {
        // Nhiệt độ vẫn vượt HighAmbientTempWarning(45) → còn anomaly → không resolve.
        var (uow, alert) = AmbientHarness(reading => reading.AmbientTemperature = 50m);
        var sut = new AlertAutoResolveService(uow.Build());

        var result = await sut.AutoResolveAsync(LookbackMinutes);

        result.Resolved.Should().Be(0);
        alert.Status.Should().Be(AlertStatusEnum.Open);
    }

    [Fact]
    public async Task AutoResolve_AmbientAlert_NoRecentReading_IsSkipped()
    {
        var now = DateTime.UtcNow;
        var siteId = Guid.NewGuid();

        var threshold = new AmbientThresholdConfig
        {
            Id = Guid.NewGuid(),
            SiteId = siteId,
            Enabled = true,
            HighAmbientTempWarning = 45m,
            HighAmbientTempCritical = 55m,
        };

        var alert = new Alert
        {
            Id = Guid.NewGuid(),
            BatteryAssetId = null,
            SiteId = siteId,
            AnomalyType = AnomalyTypeEnum.HighAmbientTemp,
            Severity = AlertSeverityEnum.Warning,
            Status = AlertStatusEnum.Open,
            DetectedAt = now.AddHours(-2),
            DedupWindowEndUtc = now.AddHours(-1),
        };

        // Không có AmbientReading nào trong cửa sổ lookback — không đủ bằng chứng để resolve.
        var uow = new MockUnitOfWorkBuilder()
            .WithAmbientThresholdConfigs(threshold)
            .WithAlerts(alert);

        var sut = new AlertAutoResolveService(uow.Build());

        var result = await sut.AutoResolveAsync(LookbackMinutes);

        result.Resolved.Should().Be(0);
        alert.Status.Should().Be(AlertStatusEnum.Open);
    }

    [Fact]
    public async Task AutoResolve_EnvironmentalIncident_IsNotResolvedByAmbientThresholdEngine()
    {
        var (uow, alert) = AmbientHarness(reading => reading.AmbientTemperature = 30m);
        alert.AnomalyType = AnomalyTypeEnum.EnvironmentalIncident;
        var sut = new AlertAutoResolveService(uow.Build());

        var result = await sut.AutoResolveAsync(LookbackMinutes);

        result.Resolved.Should().Be(0);
        alert.Status.Should().Be(AlertStatusEnum.Open);
        alert.ResolvedAt.Should().BeNull();
    }

    [Theory]
    [InlineData(AnomalyTypeEnum.Undervoltage)]
    [InlineData(AnomalyTypeEnum.Undertemp)]
    [InlineData(AnomalyTypeEnum.IotDataIntegrityViolation)]
    public async Task AutoResolve_BatteryAlertUnsupportedByThresholdEngine_IsSkipped(
        AnomalyTypeEnum anomalyType)
    {
        var (uow, alert) = Harness(anomalyType);
        var sut = new AlertAutoResolveService(uow.Build());

        var result = await sut.AutoResolveAsync(LookbackMinutes);

        result.Resolved.Should().Be(0);
        alert.Status.Should().Be(AlertStatusEnum.Open);
        alert.ResolvedAt.Should().BeNull();
    }

    /// <summary>
    /// 1 site + threshold Enabled + 1 AmbientReading gần nhất, với nhiệt độ tuỳ chỉnh qua
    /// <paramref name="configureReading"/>, kèm 1 alert HighAmbientTemp Open đủ cũ để lọt cutoff.
    /// </summary>
    private static (MockUnitOfWorkBuilder Uow, Alert Alert) AmbientHarness(Action<AmbientReading> configureReading)
    {
        var now = DateTime.UtcNow;
        var siteId = Guid.NewGuid();

        var threshold = new AmbientThresholdConfig
        {
            Id = Guid.NewGuid(),
            SiteId = siteId,
            Enabled = true,
            HighAmbientTempWarning = 45m,
            HighAmbientTempCritical = 55m,
        };

        var reading = new AmbientReading
        {
            Time = now.AddMinutes(-1),
            SiteId = siteId,
        };
        configureReading(reading);

        var alert = new Alert
        {
            Id = Guid.NewGuid(),
            BatteryAssetId = null,
            SiteId = siteId,
            AnomalyType = AnomalyTypeEnum.HighAmbientTemp,
            Severity = AlertSeverityEnum.Warning,
            Status = AlertStatusEnum.Open,
            DetectedAt = now.AddHours(-2),
            DedupWindowEndUtc = now.AddHours(-1),
        };

        var uow = new MockUnitOfWorkBuilder()
            .WithAmbientThresholdConfigs(threshold)
            .WithAmbientReadings(reading)
            .WithAlerts(alert);

        return (uow, alert);
    }

    /// <summary>
    /// 1 asset + threshold active + 1 reading mới nằm trong ngưỡng (không còn anomaly nào),
    /// kèm 1 alert Open đủ cũ để lọt qua cutoff — điều kiện để auto-resolve ra tay.
    /// </summary>
    private static (MockUnitOfWorkBuilder Uow, Alert Alert) Harness(AnomalyTypeEnum anomalyType)
    {
        var now = DateTime.UtcNow;
        var batteryTypeId = Guid.NewGuid();
        var assetId = Guid.NewGuid();

        var asset = new BatteryAsset
        {
            Id = assetId,
            SerialNumber = "SN-GH783-AR",
            BatteryTypeId = batteryTypeId,
            CustomerId = Guid.NewGuid(),
            Status = BatteryStatusEnum.Active,
        };

        var threshold = new ThresholdConfig
        {
            Id = Guid.NewGuid(),
            BatteryTypeId = batteryTypeId,
            VoltageMin = 14m,
            VoltageMax = 15m,
            TemperatureMin = 45m,
            TemperatureMax = 45m,
            SocWarningThreshold = 20m,
            SocCriticalThreshold = 10m,
            IsActive = true,
            EffectiveFromUtc = now.AddDays(-30),
        };

        // Reading bình thường → AnomalyRules.Detect() trả rỗng → "hết anomaly".
        var reading = new SensorReading
        {
            Time = now.AddMinutes(-1),
            BatteryAssetId = assetId,
            Voltage = 12.6m,
            Current = -1.0m,
            Temperature = 30m,
            SocPercent = 65m,
            SourceType = SensorReadingSourceTypeEnum.Bms,
        };

        var alert = new Alert
        {
            Id = Guid.NewGuid(),
            BatteryAssetId = assetId,
            AnomalyType = anomalyType,
            Severity = AlertSeverityEnum.Critical,
            Status = AlertStatusEnum.Open,
            DetectedAt = now.AddHours(-2),
            DedupWindowEndUtc = now.AddHours(-1),
        };

        var uow = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(asset)
            .WithThresholdConfigs(threshold)
            .WithSensorReadings(reading)
            .WithAlerts(alert);

        return (uow, alert);
    }
}
