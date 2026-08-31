using BatteryService.Application.CQRS.Command.Alert;
using BatteryService.Application.CQRS.Command.EnvironmentalIncident;
using BatteryService.Application.CQRS.Command.SensorReading;
using BatteryService.Application.CQRS.Command.ThresholdConfig;
using BatteryService.Application.CQRS.Handler.Alert;
using BatteryService.Application.CQRS.Handler.SensorReading;
using BatteryService.Application.CQRS.Handler.ThresholdConfig;
using BatteryService.Application.CQRS.Query.Alert;
using BatteryService.Application.CQRS.Query.SensorReading;
using BatteryService.Application.CQRS.Query.ThresholdConfig;
using BatteryService.Application.DTOs;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.UnitTests.Helpers;
using SharedInfrastructure.Services;

namespace BatteryService.UnitTests.Application;

public class AlertThresholdSensorHandlerTests
{
    private static readonly Guid AssetId = Guid.NewGuid();

    private static BatteryAsset MakeAsset() => new()
    {
        Id = AssetId,
        SerialNumber = "S1",
        BatteryTypeId = Guid.NewGuid(),
        CustomerId = Guid.NewGuid(),
        InstallDate = DateTime.UtcNow,
        Status = BatteryStatusEnum.Active,
        CreatedAt = DateTime.UtcNow
    };

    private static Alert MakeAlert(AlertStatusEnum status = AlertStatusEnum.Open, bool deleted = false) => new()
    {
        Id = Guid.NewGuid(),
        BatteryAssetId = AssetId,
        BatteryAsset = MakeAsset(),
        Status = status,
        AnomalyType = AnomalyTypeEnum.Overheat,
        Severity = AlertSeverityEnum.Warning,
        Unit = "C",
        ThresholdValue = 50,
        ActualValue = 60,
        DetectedAt = DateTime.UtcNow,
        DedupWindowEndUtc = DateTime.UtcNow.AddMinutes(5),
        IsDeleted = deleted,
        CreatedAt = DateTime.UtcNow
    };

    private static Alert MakeIotAlert(AnomalyTypeEnum anomalyType = AnomalyTypeEnum.DeviceOffline)
    {
        var site = new Site
        {
            Id = Guid.NewGuid(),
            Name = "Site IoT",
            CustomerId = Guid.NewGuid(),
            InstallDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        var device = new IotDevice
        {
            Id = Guid.NewGuid(),
            DeviceCode = "ESP32-001",
            DisplayName = "Gateway 1",
            SiteId = site.Id,
            Site = site,
            ApiKeyHash = "hash",
            ApiKeyLastFour = "1234",
            ApiKeyIssuedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        return new Alert
        {
            Id = Guid.NewGuid(),
            IotDeviceId = device.Id,
            IotDevice = device,
            SiteId = site.Id,
            Site = site,
            Status = AlertStatusEnum.Open,
            AnomalyType = anomalyType,
            Severity = AlertSeverityEnum.Warning,
            DetectedAt = DateTime.UtcNow,
            DedupWindowEndUtc = DateTime.UtcNow.AddMinutes(5),
            CreatedAt = DateTime.UtcNow
        };
    }

    // ===== Alert =====

    [Fact]
    public async Task Ack_NotFound_404()
    {
        var b = new MockUnitOfWorkBuilder();
        var c = TestBatteryCurrentUserService.Admin();
        var r = await new AcknowledgeAlertCommandHandler(b.Build(), c).Handle(new AcknowledgeAlertCommand { Id = Guid.NewGuid() }, default);
        r.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Ack_AlreadyResolved_409()
    {
        var a = MakeAlert(AlertStatusEnum.Resolved);
        var b = new MockUnitOfWorkBuilder().WithAlerts(a);
        var c = TestBatteryCurrentUserService.Admin();
        var r = await new AcknowledgeAlertCommandHandler(b.Build(), c).Handle(new AcknowledgeAlertCommand { Id = a.Id }, default);
        r.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Ack_Happy_SetsAck()
    {
        var a = MakeAlert();
        var b = new MockUnitOfWorkBuilder().WithAlerts(a);
        var uid = Guid.NewGuid();
        // Admin ⇒ phạm vi không giới hạn, giữ nguyên ý nghĩa test; UserId vẫn là uid để
        // assertion AcknowledgedByUserId bên dưới không đổi.
        var c = new TestBatteryCurrentUserService(uid.ToString(), "Admin");
        var r = await new AcknowledgeAlertCommandHandler(b.Build(), c).Handle(new AcknowledgeAlertCommand { Id = a.Id }, default);
        r.IsSuccess.Should().BeTrue();
        a.Status.Should().Be(AlertStatusEnum.Acknowledged);
        a.AcknowledgedByUserId.Should().Be(uid);
    }

    [Fact]
    public async Task Resolve_NotFound_404()
    {
        var r = await new ResolveAlertCommandHandler(new MockUnitOfWorkBuilder().Build()).Handle(new ResolveAlertCommand { Id = Guid.NewGuid() }, default);
        r.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Resolve_Merged_409()
    {
        var a = MakeAlert(AlertStatusEnum.Merged);
        var b = new MockUnitOfWorkBuilder().WithAlerts(a);
        var r = await new ResolveAlertCommandHandler(b.Build()).Handle(new ResolveAlertCommand { Id = a.Id }, default);
        r.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Resolve_Happy()
    {
        var a = MakeAlert();
        var b = new MockUnitOfWorkBuilder().WithAlerts(a);
        var r = await new ResolveAlertCommandHandler(b.Build()).Handle(new ResolveAlertCommand { Id = a.Id }, default);
        r.IsSuccess.Should().BeTrue();
        a.Status.Should().Be(AlertStatusEnum.Resolved);
    }

    [Fact]
    public async Task GetAlertById_NotFound_404()
    {
        var r = await new GetAlertByIdQueryHandler(new MockUnitOfWorkBuilder().Build(), TestBatteryCurrentUserService.Admin()).Handle(new GetAlertByIdQuery { Id = Guid.NewGuid() }, default);
        r.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetAlertById_Found_Dto()
    {
        var a = MakeAlert();
        var b = new MockUnitOfWorkBuilder().WithAlerts(a);
        var r = await new GetAlertByIdQueryHandler(b.Build(), TestBatteryCurrentUserService.Admin()).Handle(new GetAlertByIdQuery { Id = a.Id }, default);
        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task GetAlerts_FiltersAll()
    {
        var a = MakeAlert();
        var b = new MockUnitOfWorkBuilder().WithAlerts(a);
        var r = await new GetAlertsQueryHandler(b.Build(), TestBatteryCurrentUserService.Admin()).Handle(new GetAlertsQuery
        {
            BatteryAssetId = AssetId,
            Severity = AlertSeverityEnum.Warning,
            Status = AlertStatusEnum.Open,
            From = DateTime.UtcNow.AddDays(-1),
            To = DateTime.UtcNow.AddDays(1)
        }, default);
        r.Data!.TotalItems.Should().Be(1);
    }

    [Fact]
    public async Task GetAlertById_IotAlert_ReturnsDeviceAndSiteIdentity()
    {
        var alert = MakeIotAlert();
        var builder = new MockUnitOfWorkBuilder().WithAlerts(alert);

        var result = await new GetAlertByIdQueryHandler(builder.Build(), TestBatteryCurrentUserService.Admin())
            .Handle(new GetAlertByIdQuery { Id = alert.Id }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data!.BatterySerialNumber.Should().BeEmpty();
        result.Data.IotDeviceCode.Should().Be("ESP32-001");
        result.Data.IotDeviceName.Should().Be("Gateway 1");
        result.Data.SiteName.Should().Be("Site IoT");
    }

    [Fact]
    public async Task GetAlerts_IotOnly_ReturnsOnlyDeviceAlertTypes()
    {
        var offline = MakeIotAlert();
        var integrity = MakeIotAlert(AnomalyTypeEnum.IotDataIntegrityViolation);
        var battery = MakeAlert();
        var builder = new MockUnitOfWorkBuilder().WithAlerts(offline, integrity, battery);

        var result = await new GetAlertsQueryHandler(builder.Build(), TestBatteryCurrentUserService.Admin())
            .Handle(new GetAlertsQuery { IotOnly = true }, default);

        result.Data!.TotalItems.Should().Be(2);
        result.Data.Items.Should().OnlyContain(item =>
            item.AnomalyType == AnomalyTypeEnum.DeviceOffline
            || item.AnomalyType == AnomalyTypeEnum.IotDataIntegrityViolation);
        result.Data.Items.Should().OnlyContain(item => !string.IsNullOrWhiteSpace(item.IotDeviceCode));
    }

    [Fact]
    public async Task GetAlerts_ExcludeIotDeviceAlerts_ReturnsOnlyBatteryAlerts()
    {
        var offline = MakeIotAlert();
        var integrity = MakeIotAlert(AnomalyTypeEnum.IotDataIntegrityViolation);
        var battery = MakeAlert();
        var builder = new MockUnitOfWorkBuilder().WithAlerts(offline, integrity, battery);

        var result = await new GetAlertsQueryHandler(builder.Build(), TestBatteryCurrentUserService.Admin())
            .Handle(new GetAlertsQuery { ExcludeIotDeviceAlerts = true }, default);

        result.Data!.TotalItems.Should().Be(1);
        result.Data.Items.Should().ContainSingle(item => item.Id == battery.Id.ToString());
    }

    /// <summary>
    /// Alert ngưỡng môi trường là alert CẤP SITE: không gắn pin nên không có serial để hiện.
    /// Màn "Battery alerts" phải loại hết chúng ra.
    /// </summary>
    /// <remarks>
    /// Chốt việc lọc theo quan hệ <c>BatteryAssetId</c> thay vì liệt kê từng <c>AnomalyType</c>.
    /// Bản cũ chỉ trừ đúng <c>EnvironmentalIncident</c>, nên khi thêm <c>HighGasConcentration</c>
    /// (#18) thì alert khí gas lọt vào danh sách pin thành dòng "Site level" trống serial. Dùng
    /// chính loại mới đó làm dữ liệu test để bài test này gãy nếu ai quay lại cách liệt kê.
    /// </remarks>
    [Fact]
    public async Task GetAlerts_ExcludeEnvironmentalIncidents_DropsEverySiteLevelAlert()
    {
        var battery = MakeAlert();
        var siteId = Guid.NewGuid();
        Alert SiteLevel(AnomalyTypeEnum type) => new()
        {
            Id = Guid.NewGuid(),
            BatteryAssetId = null,
            SiteId = siteId,
            Status = AlertStatusEnum.Open,
            AnomalyType = type,
            Severity = AlertSeverityEnum.Critical,
            DetectedAt = DateTime.UtcNow,
            DedupWindowEndUtc = DateTime.UtcNow.AddMinutes(5),
            CreatedAt = DateTime.UtcNow
        };

        var builder = new MockUnitOfWorkBuilder().WithAlerts(
            battery,
            SiteLevel(AnomalyTypeEnum.EnvironmentalIncident),
            SiteLevel(AnomalyTypeEnum.HighAmbientTemp),
            SiteLevel(AnomalyTypeEnum.HighGasConcentration));

        var result = await new GetAlertsQueryHandler(builder.Build(), TestBatteryCurrentUserService.Admin())
            .Handle(new GetAlertsQuery { ExcludeEnvironmentalIncidents = true }, default);

        result.Data!.TotalItems.Should().Be(1);
        result.Data.Items.Should().ContainSingle(item => item.Id == battery.Id.ToString());
    }

    /// <summary>
    /// Ba màn hình alert (Battery / Device / Environmental) phải chia trọn tập alert và KHÔNG chồng
    /// nhau: chồng thì badge sidebar đếm một alert hai lần, hụt thì có alert không nằm ở màn nào cả.
    /// </summary>
    [Fact]
    public async Task GetAlerts_ThreeScreenFilters_PartitionAlertsExactlyOnce()
    {
        var battery = MakeAlert();
        var device = MakeIotAlert();
        var siteId = Guid.NewGuid();
        var ambient = new Alert
        {
            Id = Guid.NewGuid(),
            BatteryAssetId = null,
            SiteId = siteId,
            Status = AlertStatusEnum.Open,
            AnomalyType = AnomalyTypeEnum.HighGasConcentration,
            Severity = AlertSeverityEnum.Critical,
            DetectedAt = DateTime.UtcNow,
            DedupWindowEndUtc = DateTime.UtcNow.AddMinutes(5),
            CreatedAt = DateTime.UtcNow
        };

        async Task<List<string>> IdsFor(GetAlertsQuery q)
        {
            var handler = new GetAlertsQueryHandler(
                new MockUnitOfWorkBuilder().WithAlerts(battery, device, ambient).Build(),
                TestBatteryCurrentUserService.Admin());
            var r = await handler.Handle(q, default);
            return r.Data!.Items.Select(i => i.Id).ToList();
        }

        var batteryScreen = await IdsFor(new GetAlertsQuery { ExcludeEnvironmentalIncidents = true, ExcludeIotDeviceAlerts = true });
        var deviceScreen = await IdsFor(new GetAlertsQuery { IotOnly = true });
        var envScreen = await IdsFor(new GetAlertsQuery { SiteLevelOnly = true });

        batteryScreen.Should().Equal(battery.Id.ToString());
        deviceScreen.Should().Equal(device.Id.ToString());
        envScreen.Should().Equal(ambient.Id.ToString());

        batteryScreen.Concat(deviceScreen).Concat(envScreen)
            .Should().OnlyHaveUniqueItems("không alert nào được đếm ở hai màn")
            .And.HaveCount(3, "và không alert nào bị bỏ sót");
    }

    /// <summary>
    /// Màn hình "Environmental alerts" đọc MỘT nguồn duy nhất, nên <c>SiteLevelOnly</c> phải trả về
    /// cả hai thứ nó cần hiện: sự cố do firmware báo (dưới dạng alert bản sao) và vượt ngưỡng do
    /// backend phát hiện.
    /// </summary>
    /// <remarks>
    /// Bản sao mang <c>AnomalyType = EnvironmentalIncident</c> và không có số đo, nên nếu chỉ dựa
    /// vào AnomalyType thì mọi sự cố đều hiện chung một dòng vô nghĩa "Environmental incident /
    /// 0 incident". Vì vậy DTO phải kèm <c>IncidentType</c> để dòng đó hiện đúng "Gas leak".
    /// </remarks>
    [Fact]
    public async Task GetAlerts_SiteLevelOnly_KeepsIncidentMirrorAndExposesItsIncidentType()
    {
        var siteId = Guid.NewGuid();
        var incident = new EnvironmentalIncident
        {
            Id = Guid.NewGuid(),
            SiteId = siteId,
            IncidentType = EnvironmentalIncidentTypeEnum.GasLeak,
            Severity = AlertSeverityEnum.Critical,
            DetectedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        Alert SiteLevel(AnomalyTypeEnum type, EnvironmentalIncident? from = null) => new()
        {
            Id = Guid.NewGuid(),
            BatteryAssetId = null,
            SiteId = siteId,
            EnvironmentalIncidentId = from?.Id,
            EnvironmentalIncident = from,
            Status = AlertStatusEnum.Open,
            AnomalyType = type,
            Severity = AlertSeverityEnum.Critical,
            DetectedAt = DateTime.UtcNow,
            DedupWindowEndUtc = DateTime.UtcNow.AddMinutes(5),
            CreatedAt = DateTime.UtcNow
        };

        var mirror = SiteLevel(AnomalyTypeEnum.EnvironmentalIncident, incident);
        var gas = SiteLevel(AnomalyTypeEnum.HighGasConcentration);

        var handler = new GetAlertsQueryHandler(
            new MockUnitOfWorkBuilder().WithAlerts(mirror, gas).Build(),
            TestBatteryCurrentUserService.Admin());
        var result = await handler.Handle(new GetAlertsQuery { SiteLevelOnly = true }, default);

        result.Data!.TotalItems.Should().Be(2, "một màn hình, một nguồn — không bỏ sót sự cố nào");

        var mirrorDto = result.Data.Items.Single(i => i.Id == mirror.Id.ToString());
        mirrorDto.IncidentType.Should().Be(EnvironmentalIncidentTypeEnum.GasLeak,
            "thiếu trường này thì dòng hiện 'Environmental incident' thay vì 'Gas leak'");
        mirrorDto.EnvironmentalIncidentId.Should().Be(incident.Id.ToString());

        result.Data.Items.Single(i => i.Id == gas.Id.ToString())
            .IncidentType.Should().BeNull("alert vượt ngưỡng không đến từ sự cố nào");
    }

    // ===== ThresholdConfig =====

    [Fact]
    public async Task Upsert_BatteryTypeMissing_404()
    {
        var b = new MockUnitOfWorkBuilder();
        var r = await new UpsertThresholdConfigCommandHandler(b.Build(), Moq.Mock.Of<MediatR.IPublisher>()).Handle(new UpsertThresholdConfigCommand
        {
            BatteryTypeId = Guid.NewGuid(),
            VoltageMin = 14,
            VoltageMax = 20,
            TemperatureMin = 45,
            TemperatureMax = 40,
            SocWarningThreshold = 30,
            SocCriticalThreshold = 10
        }, default);
        r.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Upsert_CreatesNew_WhenNoneExists()
    {
        var t = new BatteryType { Id = Guid.NewGuid(), Name = "T", NominalCapacityAh = 1, NominalVoltage = 1, CreatedAt = DateTime.UtcNow };
        var b = new MockUnitOfWorkBuilder().WithBatteryTypes(t);
        var r = await new UpsertThresholdConfigCommandHandler(b.Build(), Moq.Mock.Of<MediatR.IPublisher>()).Handle(new UpsertThresholdConfigCommand
        {
            BatteryTypeId = t.Id,
            VoltageMin = 14,
            VoltageMax = 20,
            TemperatureMin = 45,
            TemperatureMax = 40,
            SocWarningThreshold = 30,
            SocCriticalThreshold = 10,
            EffectiveFromUtc = DateTime.UtcNow,
            CurrentMaxCharge = 5,
            CurrentMaxDischarge = 5
        }, default);
        r.IsSuccess.Should().BeTrue();
        b.ThresholdConfigs.Verify(x => x.AddAsync(It.IsAny<ThresholdConfig>()), Times.Once);
    }

    [Fact]
    public async Task Upsert_UpdatesExisting()
    {
        var t = new BatteryType { Id = Guid.NewGuid(), Name = "T", NominalCapacityAh = 1, NominalVoltage = 1, CreatedAt = DateTime.UtcNow };
        var existing = new ThresholdConfig { Id = Guid.NewGuid(), BatteryTypeId = t.Id, BatteryType = t, IsActive = true, VoltageMin = 5, VoltageMax = 8, TemperatureMin = -5, TemperatureMax = 30, SocWarningThreshold = 25, SocCriticalThreshold = 5, EffectiveFromUtc = DateTime.UtcNow, CreatedAt = DateTime.UtcNow };
        var b = new MockUnitOfWorkBuilder().WithBatteryTypes(t).WithThresholdConfigs(existing);
        var r = await new UpsertThresholdConfigCommandHandler(b.Build(), Moq.Mock.Of<MediatR.IPublisher>()).Handle(new UpsertThresholdConfigCommand
        {
            BatteryTypeId = t.Id,
            VoltageMin = 11,
            VoltageMax = 22,
            TemperatureMin = 45,
            TemperatureMax = 40,
            SocWarningThreshold = 30,
            SocCriticalThreshold = 10
        }, default);
        r.IsSuccess.Should().BeTrue();
        existing.VoltageMin.Should().Be(11);
        b.ThresholdConfigs.Verify(x => x.UpdateAsync(It.IsAny<ThresholdConfig>()), Times.Once);
    }

    [Fact]
    public async Task GetThresholdByType_NotConfigured_Returns200WithNullData()
    {
        var r = await new GetThresholdConfigByBatteryTypeQueryHandler(new MockUnitOfWorkBuilder().Build()).Handle(new GetThresholdConfigByBatteryTypeQuery { BatteryTypeId = Guid.NewGuid() }, default);
        r.StatusCode.Should().Be(200);
        r.IsSuccess.Should().BeTrue();
        r.Data.Should().BeNull();
    }

    [Fact]
    public async Task GetThresholdByType_IncludesInactiveWhenRequested()
    {
        var t = new BatteryType { Id = Guid.NewGuid(), Name = "T", NominalCapacityAh = 1, NominalVoltage = 1, CreatedAt = DateTime.UtcNow };
        var inactive = new ThresholdConfig { Id = Guid.NewGuid(), BatteryTypeId = t.Id, BatteryType = t, IsActive = false, EffectiveFromUtc = DateTime.UtcNow, CreatedAt = DateTime.UtcNow };
        var b = new MockUnitOfWorkBuilder().WithBatteryTypes(t).WithThresholdConfigs(inactive);
        var rNoInactive = await new GetThresholdConfigByBatteryTypeQueryHandler(b.Build()).Handle(new GetThresholdConfigByBatteryTypeQuery { BatteryTypeId = t.Id, IncludeInactive = false }, default);
        rNoInactive.StatusCode.Should().Be(200);
        rNoInactive.Data.Should().BeNull();
        var rWithInactive = await new GetThresholdConfigByBatteryTypeQueryHandler(b.Build()).Handle(new GetThresholdConfigByBatteryTypeQuery { BatteryTypeId = t.Id, IncludeInactive = true }, default);
        rWithInactive.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task GetThresholds_Filters()
    {
        var t = new BatteryType { Id = Guid.NewGuid(), Name = "T", NominalCapacityAh = 1, NominalVoltage = 1, CreatedAt = DateTime.UtcNow };
        var c = new ThresholdConfig { Id = Guid.NewGuid(), BatteryTypeId = t.Id, BatteryType = t, IsActive = true, EffectiveFromUtc = DateTime.UtcNow, CreatedAt = DateTime.UtcNow };
        var b = new MockUnitOfWorkBuilder().WithThresholdConfigs(c);
        var r = await new GetThresholdConfigsQueryHandler(b.Build()).Handle(new GetThresholdConfigsQuery { BatteryTypeId = t.Id, IsActive = true }, default);
        r.Data!.TotalItems.Should().Be(1);
    }

    // ===== SensorReading =====

    [Fact]
    public async Task BatchIngest_SkipsUnknownAssets()
    {
        var asset = MakeAsset();
        var b = new MockUnitOfWorkBuilder().WithBatteryAssets(asset);
        var r = await new BatchIngestSensorReadingsCommandHandler(b.Build(), new BatteryService.UnitTests.Helpers.NoopIotMetricsRecorder(), new BatteryService.UnitTests.Helpers.NoopIotCalibrationCache(), new BatteryService.UnitTests.Helpers.NoopTelemetryPublisher(), new BatteryService.UnitTests.Helpers.NoopTelemetryStatsService(), Microsoft.Extensions.Logging.Abstractions.NullLogger<BatchIngestSensorReadingsCommandHandler>.Instance).Handle(new BatchIngestSensorReadingsCommand
        {
            Items = new List<SensorReadingItem>
            {
                new() { BatteryAssetId = asset.Id, Time = DateTime.UtcNow, Voltage = 12, Current = 1, Temperature = 25, SocPercent = 80 },
                new() { BatteryAssetId = Guid.NewGuid(), Time = DateTime.UtcNow, Voltage = 12, Current = 1, Temperature = 25, SocPercent = 80 }
            }
        }, default);
        r.IsSuccess.Should().BeTrue();
        r.Data!.TotalReceived.Should().Be(2);
        r.Data.Inserted.Should().Be(1);
        r.Data.Skipped.Should().Be(1);
        asset.LastSensorReadingAt.Should().NotBeNull();
    }

    [Fact]
    public async Task GetLatest_NotFound_404()
    {
        var r = await new GetLatestSensorReadingQueryHandler(new MockUnitOfWorkBuilder().Build(), TestBatteryCurrentUserService.Admin()).Handle(new GetLatestSensorReadingQuery { BatteryAssetId = Guid.NewGuid() }, default);
        r.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetLatest_ReturnsLatestByTime()
    {
        var assetId = Guid.NewGuid();
        var older = new SensorReading { BatteryAssetId = assetId, Time = DateTime.UtcNow.AddHours(-1), Voltage = 10, Current = 0, Temperature = 25, SocPercent = 50 };
        var newer = new SensorReading { BatteryAssetId = assetId, Time = DateTime.UtcNow, Voltage = 12, Current = 0, Temperature = 26, SocPercent = 60 };
        var b = new MockUnitOfWorkBuilder().WithSensorReadings(older, newer);
        var r = await new GetLatestSensorReadingQueryHandler(b.Build(), TestBatteryCurrentUserService.Admin()).Handle(new GetLatestSensorReadingQuery { BatteryAssetId = assetId }, default);
        r.IsSuccess.Should().BeTrue();
        r.Data!.Voltage.Should().Be(12);
    }

    [Fact]
    public async Task GetHistory_FiltersByTimeRange()
    {
        var assetId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var inRange = new SensorReading { BatteryAssetId = assetId, Time = now.AddHours(-1), Voltage = 12, Current = 0, Temperature = 25, SocPercent = 50 };
        var outOfRange = new SensorReading { BatteryAssetId = assetId, Time = now.AddDays(-5), Voltage = 11, Current = 0, Temperature = 24, SocPercent = 40 };
        var b = new MockUnitOfWorkBuilder().WithSensorReadings(inRange, outOfRange);
        var r = await new GetSensorReadingHistoryQueryHandler(b.Build(), TestBatteryCurrentUserService.Admin()).Handle(new GetSensorReadingHistoryQuery
        {
            BatteryAssetId = assetId,
            From = now.AddDays(-2),
            To = now.AddHours(1)
        }, default);
        r.Data!.Items.Should().ContainSingle();
        r.Data.HasMore.Should().BeFalse();
        r.Data.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task GetHistory_UsesCursorAndLimit()
    {
        var assetId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var newest = new SensorReading { BatteryAssetId = assetId, Time = now, Voltage = 13, Current = 0, Temperature = 27, SocPercent = 70 };
        var middle = new SensorReading { BatteryAssetId = assetId, Time = now.AddMinutes(-1), Voltage = 12, Current = 0, Temperature = 26, SocPercent = 60 };
        var oldest = new SensorReading { BatteryAssetId = assetId, Time = now.AddMinutes(-2), Voltage = 11, Current = 0, Temperature = 25, SocPercent = 50 };
        var b = new MockUnitOfWorkBuilder().WithSensorReadings(newest, middle, oldest);

        var firstPage = await new GetSensorReadingHistoryQueryHandler(b.Build(), TestBatteryCurrentUserService.Admin()).Handle(new GetSensorReadingHistoryQuery
        {
            BatteryAssetId = assetId,
            Limit = 1
        }, default);

        firstPage.Data!.Items.Should().ContainSingle();
        firstPage.Data.Items[0].Time.Should().Be(newest.Time);
        firstPage.Data.HasMore.Should().BeTrue();
        firstPage.Data.NextCursor.Should().Be(newest.Time);

        var secondPage = await new GetSensorReadingHistoryQueryHandler(b.Build(), TestBatteryCurrentUserService.Admin()).Handle(new GetSensorReadingHistoryQuery
        {
            BatteryAssetId = assetId,
            Limit = 1,
            Cursor = firstPage.Data.NextCursor
        }, default);

        secondPage.Data!.Items.Should().ContainSingle();
        secondPage.Data.Items[0].Time.Should().Be(middle.Time);
    }
}
