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
