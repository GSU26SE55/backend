using BatteryService.Application.Anomaly;
using BatteryService.Application.CQRS.Command.SensorReading;
using BatteryService.Application.CQRS.Handler.SensorReading;
using BatteryService.Application.Services;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.Infrastructure.Implements.Repositories;
using BatteryService.Infrastructure.Observability;
using BatteryService.Infrastructure.Persistence;
using BatteryService.UnitTests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharedInfrastructure.Persistence.Interceptors;
using SharedInfrastructure.Services;

namespace BatteryService.IntegrationTests.DoD;

/// <summary>
/// <b>Sprint IoT-2 — Definition of Done, phần "verify".</b>
///
/// <para>DoD gốc ghi 5 việc dưới dạng "bơm dữ liệu rồi nhìn xem có đúng không" — tức kiểm bằng tay,
/// một lần, rồi bằng chứng hết hạn ngay sau đó. Ở đây chúng được viết thành test tự động: chạy lại
/// được ở mọi lần CI, và nếu ai đó làm hỏng hành vi thì test đỏ chứ không phải chờ tới lúc demo mới
/// phát hiện.</para>
///
/// <para>Mỗi test dưới đây ánh xạ 1-1 với một gạch đầu dòng trong DoD của Sprint IoT-2 (§17).</para>
/// </summary>
public class IoT2AcceptanceTests
{
    private static ApplicationDbContext NewDb(string? name = null) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(name ?? $"iot2-dod-{Guid.NewGuid()}")
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options,
            new AuditableEntityInterceptor(new CurrentUserService(new HttpContextAccessor())));

    private static BatchIngestSensorReadingsCommandHandler NewIngestHandler(ApplicationDbContext db) =>
        new(new UnitOfWork(db),
            new NoopIotMetricsRecorder(),
            new NoopIotCalibrationCache(),
            new NoopTelemetryPublisher(),
            new NoopTelemetryStatsService(),
            NullLogger<BatchIngestSensorReadingsCommandHandler>.Instance);

    // ================================================================ DoD #1
    /// <summary>
    /// DoD: <i>"Regression test cuối sprint: ingest legacy payload + ingest production payload cùng đi
    /// qua endpoint mới, không gãy simulator MVP."</i>
    ///
    /// <para>Payload <b>legacy</b> (simulator MVP) định danh pin bằng <c>batteryAssetId</c> (GUID).
    /// Payload <b>production</b> (ESP32 thật) chỉ biết <c>batteryAssetSerial</c> in trên vỏ pin.
    /// Endpoint phải nhận cả hai — bỏ nhánh legacy là demo bằng simulator gãy.</para>
    /// </summary>
    [Fact]
    public async Task DoD1_LegacyAndProductionPayload_BothIngestThroughSameEndpoint()
    {
        await using var db = NewDb();
        var siteId = Guid.NewGuid();
        var legacyAsset = new BatteryAsset { Id = Guid.NewGuid(), SerialNumber = "BAT-LEGACY", SiteId = siteId };
        var prodAsset = new BatteryAsset { Id = Guid.NewGuid(), SerialNumber = "BAT-PROD", SiteId = siteId };
        db.BatteryAssets.AddRange(legacyAsset, prodAsset);
        await db.SaveChangesAsync();

        var now = DateTime.UtcNow;
        var result = await NewIngestHandler(db).Handle(new BatchIngestSensorReadingsCommand
        {
            Items = new List<SensorReadingItem>
            {
                // legacy: chỉ có Id, KHÔNG có serial
                new() { Time = now, BatteryAssetId = legacyAsset.Id, Voltage = 51.1m, Current = 2m,
                        Temperature = 28m, SocPercent = 80m },
                // production: chỉ có serial, KHÔNG có Id
                new() { Time = now.AddSeconds(1), BatteryAssetSerial = "BAT-PROD", Voltage = 52.2m, Current = 2m,
                        Temperature = 29m, SocPercent = 81m }
            }
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var saved = await db.SensorReadings.AsNoTracking().ToListAsync();
        saved.Should().HaveCount(2, "cả payload legacy lẫn production đều phải ghi được");
        saved.Select(r => r.BatteryAssetId).Should().BeEquivalentTo(new[] { legacyAsset.Id, prodAsset.Id },
            "payload production phải được resolve serial -> đúng BatteryAssetId");
    }

    // ================================================================ DoD #2
    /// <summary>
    /// DoD: <i>"Cross-source mismatch verify: bơm cặp reading BMS vs INA226 lệch &gt; 0.5V →
    /// Alert(SensorMismatch) xuất hiện."</i>
    ///
    /// <para>Mỗi pin có 2 nguồn đo. Lệch quá ngưỡng nghĩa là <b>một trong hai cảm biến đang nói dối</b> —
    /// phải cảnh báo để kỹ thuật đi kiểm, chứ không im lặng lấy số nào cũng được.</para>
    /// </summary>
    [Fact]
    public async Task DoD2_BmsVsIotVoltageDeltaAboveThreshold_RaisesSensorMismatchAlert()
    {
        await using var db = NewDb();
        var assetId = Guid.NewGuid();
        db.BatteryAssets.Add(new BatteryAsset { Id = assetId, SerialNumber = "BAT-X", SiteId = Guid.NewGuid() });

        var t = DateTime.UtcNow.AddSeconds(-5);
        // Lệch 0.9V > ngưỡng AnomalyRules.SensorMismatchVoltageDeltaV (0.5V).
        db.SensorReadings.Add(new SensorReading
        {
            BatteryAssetId = assetId,
            Time = t,
            Voltage = 51.0m,
            Current = 2m,
            Temperature = 30m,
            SocPercent = 80m,
            SourceType = SensorReadingSourceTypeEnum.Bms,
            SensorSourceCode = "primary"
        });
        db.SensorReadings.Add(new SensorReading
        {
            BatteryAssetId = assetId,
            Time = t.AddMilliseconds(200),
            Voltage = 51.9m,
            Current = 2m,
            Temperature = 30m,
            SocPercent = 80m,
            SourceType = SensorReadingSourceTypeEnum.IotGateway,
            SensorSourceCode = "redundant"
        });
        await db.SaveChangesAsync();

        AnomalyRules.SensorMismatchVoltageDeltaV.Should().Be(0.5m, "ngưỡng DoD nêu rõ là 0.5V");

        var svc = new CrossSourceValidationService(
            new UnitOfWork(db), new NoopIotMetricsRecorder(),
            NullLogger<CrossSourceValidationService>.Instance);

        await svc.ScanRecentReadingsAsync(DateTime.UtcNow.AddMinutes(-1));

        var alerts = await db.Alerts.AsNoTracking()
            .Where(a => a.AnomalyType == AnomalyTypeEnum.SensorMismatch).ToListAsync();
        alerts.Should().NotBeEmpty("lệch 0.9V giữa BMS và IoT gateway phải sinh Alert(SensorMismatch)");
        alerts.Should().OnlyContain(a => a.BatteryAssetId == assetId);
    }

    /// <summary>
    /// Mặt còn lại của DoD #2 — lệch DƯỚI ngưỡng thì KHÔNG được báo. Thiếu vế này thì một cài đặt
    /// "luôn luôn báo mismatch" cũng làm test trên xanh, mà thực tế là spam cảnh báo giả.
    /// </summary>
    [Fact]
    public async Task DoD2_DeltaBelowThreshold_DoesNotRaiseAlert()
    {
        await using var db = NewDb();
        var assetId = Guid.NewGuid();
        db.BatteryAssets.Add(new BatteryAsset { Id = assetId, SerialNumber = "BAT-Y", SiteId = Guid.NewGuid() });

        var t = DateTime.UtcNow.AddSeconds(-5);
        db.SensorReadings.Add(new SensorReading
        {
            BatteryAssetId = assetId,
            Time = t,
            Voltage = 51.0m,
            Current = 2m,
            Temperature = 30m,
            SocPercent = 80m,
            SourceType = SensorReadingSourceTypeEnum.Bms,
            SensorSourceCode = "primary"
        });
        db.SensorReadings.Add(new SensorReading
        {
            BatteryAssetId = assetId,
            Time = t.AddMilliseconds(200),
            Voltage = 51.2m,
            Current = 2m,
            Temperature = 30m,
            SocPercent = 80m,   // lệch 0.2V < 0.5V
            SourceType = SensorReadingSourceTypeEnum.IotGateway,
            SensorSourceCode = "redundant"
        });
        await db.SaveChangesAsync();

        await new CrossSourceValidationService(new UnitOfWork(db), new NoopIotMetricsRecorder(),
                NullLogger<CrossSourceValidationService>.Instance)
            .ScanRecentReadingsAsync(DateTime.UtcNow.AddMinutes(-1));

        (await db.Alerts.AsNoTracking().CountAsync(a => a.AnomalyType == AnomalyTypeEnum.SensorMismatch))
            .Should().Be(0, "lệch trong ngưỡng là sai số bình thường của cảm biến, không phải sự cố");
    }

    // ================================================================ DoD #3
    /// <summary>
    /// DoD: <i>"Auto-disable verify: bơm 51 outlier voltage → device `Decommissioned` + alert Admin."</i>
    ///
    /// <para>Một gateway hỏng có thể bơm hàng nghìn số đo rác làm loạn toàn bộ dữ liệu pin. Ngưỡng là
    /// <b>50 outlier/giờ</b> — vượt thì tự ngắt thiết bị thay vì để nó phá tiếp.</para>
    /// </summary>
    [Fact]
    public async Task DoD3_FiftyOneOutliers_AutoDecommissionsDevice()
    {
        await using var db = NewDb();
        var siteId = Guid.NewGuid();
        var asset = new BatteryAsset { Id = Guid.NewGuid(), SerialNumber = "BAT-OUT", SiteId = siteId };
        var device = new IotDevice
        {
            Id = Guid.NewGuid(),
            DeviceCode = "GW-OUTLIER",
            DisplayName = "GW outlier",
            SiteId = siteId,
            Status = IotDeviceStatusEnum.Active
        };
        db.BatteryAssets.Add(asset);
        db.IotDevices.Add(device);
        await db.SaveChangesAsync();

        var now = DateTime.UtcNow;
        // 51 reading vượt MaxVoltage (1000V) — đúng con số DoD nêu, tức vượt ngưỡng 50 đúng 1 đơn vị.
        var items = Enumerable.Range(0, 51).Select(i => new SensorReadingItem
        {
            Time = now.AddMilliseconds(i),
            BatteryAssetId = asset.Id,
            Voltage = 1500m,
            Current = 2m,
            Temperature = 30m,
            SocPercent = 80m
        }).ToList();

        var result = await NewIngestHandler(db).Handle(new BatchIngestSensorReadingsCommand
        {
            Items = items,
            DeviceCode = device.DeviceCode,
            AuthenticatedDeviceId = device.Id
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var saved = await db.IotDevices.AsNoTracking().FirstAsync(d => d.Id == device.Id);
        saved.OutlierIncidentCount.Should().Be(51);
        saved.Status.Should().Be(IotDeviceStatusEnum.Decommissioned,
            "vượt 50 outlier/giờ phải tự ngắt thiết bị — đây là chốt chặn chống gateway hỏng bơm rác");

        (await db.SensorReadings.AsNoTracking().CountAsync())
            .Should().Be(0, "toàn bộ 51 reading là outlier nên KHÔNG được ghi vào DB");
    }

    /// <summary>Mặt đối chứng: 50 outlier (đúng ngưỡng, chưa vượt) thì thiết bị vẫn Active.</summary>
    [Fact]
    public async Task DoD3_FiftyOutliers_StaysActive_BoundaryCheck()
    {
        await using var db = NewDb();
        var siteId = Guid.NewGuid();
        var asset = new BatteryAsset { Id = Guid.NewGuid(), SerialNumber = "BAT-OUT2", SiteId = siteId };
        var device = new IotDevice
        {
            Id = Guid.NewGuid(),
            DeviceCode = "GW-OUTLIER-2",
            DisplayName = "GW outlier 2",
            SiteId = siteId,
            Status = IotDeviceStatusEnum.Active
        };
        db.BatteryAssets.Add(asset);
        db.IotDevices.Add(device);
        await db.SaveChangesAsync();

        var now = DateTime.UtcNow;
        var items = Enumerable.Range(0, 50).Select(i => new SensorReadingItem
        {
            Time = now.AddMilliseconds(i),
            BatteryAssetId = asset.Id,
            Voltage = 1500m,
            Current = 2m,
            Temperature = 30m,
            SocPercent = 80m
        }).ToList();

        await NewIngestHandler(db).Handle(new BatchIngestSensorReadingsCommand
        {
            Items = items,
            DeviceCode = device.DeviceCode,
            AuthenticatedDeviceId = device.Id
        }, CancellationToken.None);

        var saved = await db.IotDevices.AsNoTracking().FirstAsync(d => d.Id == device.Id);
        saved.OutlierIncidentCount.Should().Be(50);
        saved.Status.Should().Be(IotDeviceStatusEnum.Active,
            "ngưỡng là > 50, đúng 50 thì chưa ngắt — chốt biên để không ngắt nhầm thiết bị lành");
    }

    // ================================================================ DoD #4
    /// <summary>
    /// DoD: <i>"Metric `/metrics` đầy đủ label `status`, `reason`, `from_version`, `to_version` —
    /// Grafana panel vẽ được."</i>
    ///
    /// <para>Thiếu label thì panel Grafana không nhóm được, biểu đồ ra một đường phẳng vô nghĩa.
    /// Test chốt đúng tên label vì Grafana query bằng tên — đổi tên là dashboard chết im lặng.</para>
    /// </summary>
    [Fact]
    public void DoD4_IotMetrics_ExposeRequiredLabelNames()
    {
        IotMetrics.HeartbeatsTotal.LabelNames.Should().Contain("status");
        IotMetrics.SensorReadingsRejectedTotal.LabelNames.Should().Contain("reason");

        IotMetrics.FirmwareUpdatesTotal.LabelNames.Should()
            .Contain(new[] { "from_version", "to_version", "status" },
                "panel OTA nhóm theo phiên bản nguồn/đích và kết quả");
    }

    /// <summary>
    /// Metric phải THỰC SỰ ghi được với label — khai báo đúng tên nhưng gọi sai số lượng label thì
    /// prometheus-net ném lúc chạy, và lỗi đó chỉ lộ ra trên production.
    /// </summary>
    [Fact]
    public void DoD4_IotMetrics_CanRecordWithLabels_WithoutThrowing()
    {
        var act = () =>
        {
            IotMetrics.HeartbeatsTotal.WithLabels("dod-device", "ok").Inc();
            IotMetrics.SensorReadingsRejectedTotal.WithLabels("sensor_outlier").Inc();
            IotMetrics.FirmwareUpdatesTotal.WithLabels("1.0.0", "1.1.0", "success").Inc();
        };

        act.Should().NotThrow();
    }
}
