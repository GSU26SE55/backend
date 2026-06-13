using BatteryService.Application.CQRS.Command.Alert;
using BatteryService.Application.CQRS.Command.SensorReading;
using BatteryService.Application.CQRS.Command.ThresholdConfig;
using BatteryService.Application.CQRS.Handler.Alert;
using BatteryService.Application.CQRS.Handler.SensorReading;
using BatteryService.Application.CQRS.Handler.ThresholdConfig;
using BatteryService.Application.CQRS.Query.Alert;
using BatteryService.Application.CQRS.Query.SensorReading;
using BatteryService.Application.CQRS.Query.ThresholdConfig;
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

    // ===== Alert =====

    [Fact]
    public async Task Ack_NotFound_404()
    {
        var b = new MockUnitOfWorkBuilder();
        var c = new Mock<ICurrentUserService>();
        var r = await new AcknowledgeAlertCommandHandler(b.Build(), c.Object).Handle(new AcknowledgeAlertCommand { Id = Guid.NewGuid() }, default);
        r.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Ack_AlreadyResolved_409()
    {
        var a = MakeAlert(AlertStatusEnum.Resolved);
        var b = new MockUnitOfWorkBuilder().WithAlerts(a);
        var c = new Mock<ICurrentUserService>();
        var r = await new AcknowledgeAlertCommandHandler(b.Build(), c.Object).Handle(new AcknowledgeAlertCommand { Id = a.Id }, default);
        r.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Ack_Happy_SetsAck()
    {
        var a = MakeAlert();
        var b = new MockUnitOfWorkBuilder().WithAlerts(a);
        var uid = Guid.NewGuid();
        var c = new Mock<ICurrentUserService>();
        c.SetupGet(x => x.UserId).Returns(uid.ToString());
        var r = await new AcknowledgeAlertCommandHandler(b.Build(), c.Object).Handle(new AcknowledgeAlertCommand { Id = a.Id }, default);
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
        var r = await new GetAlertByIdQueryHandler(new MockUnitOfWorkBuilder().Build()).Handle(new GetAlertByIdQuery { Id = Guid.NewGuid() }, default);
        r.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetAlertById_Found_Dto()
    {
        var a = MakeAlert();
        var b = new MockUnitOfWorkBuilder().WithAlerts(a);
        var r = await new GetAlertByIdQueryHandler(b.Build()).Handle(new GetAlertByIdQuery { Id = a.Id }, default);
        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task GetAlerts_FiltersAll()
    {
        var a = MakeAlert();
        var b = new MockUnitOfWorkBuilder().WithAlerts(a);
        var r = await new GetAlertsQueryHandler(b.Build()).Handle(new GetAlertsQuery
        {
            BatteryAssetId = AssetId,
            Severity = AlertSeverityEnum.Warning,
            Status = AlertStatusEnum.Open,
            From = DateTime.UtcNow.AddDays(-1),
            To = DateTime.UtcNow.AddDays(1)
        }, default);
        r.Data!.TotalItems.Should().Be(1);
    }

    // ===== ThresholdConfig =====

    [Fact]
    public async Task Upsert_BatteryTypeMissing_404()
    {
        var b = new MockUnitOfWorkBuilder();
        var r = await new UpsertThresholdConfigCommandHandler(b.Build()).Handle(new UpsertThresholdConfigCommand
        {
            BatteryTypeId = Guid.NewGuid(),
            VoltageMin = 10,
            VoltageMax = 20,
            TemperatureMin = -10,
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
        var r = await new UpsertThresholdConfigCommandHandler(b.Build()).Handle(new UpsertThresholdConfigCommand
        {
            BatteryTypeId = t.Id,
            VoltageMin = 10,
            VoltageMax = 20,
            TemperatureMin = -10,
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
        var r = await new UpsertThresholdConfigCommandHandler(b.Build()).Handle(new UpsertThresholdConfigCommand
        {
            BatteryTypeId = t.Id,
            VoltageMin = 11,
            VoltageMax = 22,
            TemperatureMin = -10,
            TemperatureMax = 40,
            SocWarningThreshold = 30,
            SocCriticalThreshold = 10
        }, default);
        r.IsSuccess.Should().BeTrue();
        existing.VoltageMin.Should().Be(11);
        b.ThresholdConfigs.Verify(x => x.UpdateAsync(It.IsAny<ThresholdConfig>()), Times.Once);
    }

    [Fact]
    public async Task GetThresholdByType_NotFound_404()
    {
        var r = await new GetThresholdConfigByBatteryTypeQueryHandler(new MockUnitOfWorkBuilder().Build()).Handle(new GetThresholdConfigByBatteryTypeQuery { BatteryTypeId = Guid.NewGuid() }, default);
        r.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetThresholdByType_IncludesInactiveWhenRequested()
    {
        var t = new BatteryType { Id = Guid.NewGuid(), Name = "T", NominalCapacityAh = 1, NominalVoltage = 1, CreatedAt = DateTime.UtcNow };
        var inactive = new ThresholdConfig { Id = Guid.NewGuid(), BatteryTypeId = t.Id, BatteryType = t, IsActive = false, EffectiveFromUtc = DateTime.UtcNow, CreatedAt = DateTime.UtcNow };
        var b = new MockUnitOfWorkBuilder().WithBatteryTypes(t).WithThresholdConfigs(inactive);
        var rNoInactive = await new GetThresholdConfigByBatteryTypeQueryHandler(b.Build()).Handle(new GetThresholdConfigByBatteryTypeQuery { BatteryTypeId = t.Id, IncludeInactive = false }, default);
        rNoInactive.StatusCode.Should().Be(404);
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
        var r = await new BatchIngestSensorReadingsCommandHandler(b.Build(), new BatteryService.UnitTests.Helpers.NoopIotMetricsRecorder(), new BatteryService.UnitTests.Helpers.NoopIotCalibrationCache(), Microsoft.Extensions.Logging.Abstractions.NullLogger<BatchIngestSensorReadingsCommandHandler>.Instance).Handle(new BatchIngestSensorReadingsCommand
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
        var r = await new GetLatestSensorReadingQueryHandler(new MockUnitOfWorkBuilder().Build()).Handle(new GetLatestSensorReadingQuery { BatteryAssetId = Guid.NewGuid() }, default);
        r.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetLatest_ReturnsLatestByTime()
    {
        var assetId = Guid.NewGuid();
        var older = new SensorReading { BatteryAssetId = assetId, Time = DateTime.UtcNow.AddHours(-1), Voltage = 10, Current = 0, Temperature = 25, SocPercent = 50 };
        var newer = new SensorReading { BatteryAssetId = assetId, Time = DateTime.UtcNow, Voltage = 12, Current = 0, Temperature = 26, SocPercent = 60 };
        var b = new MockUnitOfWorkBuilder().WithSensorReadings(older, newer);
        var r = await new GetLatestSensorReadingQueryHandler(b.Build()).Handle(new GetLatestSensorReadingQuery { BatteryAssetId = assetId }, default);
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
        var r = await new GetSensorReadingHistoryQueryHandler(b.Build()).Handle(new GetSensorReadingHistoryQuery
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

        var firstPage = await new GetSensorReadingHistoryQueryHandler(b.Build()).Handle(new GetSensorReadingHistoryQuery
        {
            BatteryAssetId = assetId,
            Limit = 1
        }, default);

        firstPage.Data!.Items.Should().ContainSingle();
        firstPage.Data.Items[0].Time.Should().Be(newest.Time);
        firstPage.Data.HasMore.Should().BeTrue();
        firstPage.Data.NextCursor.Should().Be(newest.Time);

        var secondPage = await new GetSensorReadingHistoryQueryHandler(b.Build()).Handle(new GetSensorReadingHistoryQuery
        {
            BatteryAssetId = assetId,
            Limit = 1,
            Cursor = firstPage.Data.NextCursor
        }, default);

        secondPage.Data!.Items.Should().ContainSingle();
        secondPage.Data.Items[0].Time.Should().Be(middle.Time);
    }
}
