using BatteryService.Application.Anomaly;
using BatteryService.Application.CQRS.Command.Ambient;
using BatteryService.Application.CQRS.Handler.Ambient;
using BatteryService.Application.CQRS.Query.Ambient;
using BatteryService.Application.Interfaces;
using Microsoft.Extensions.Options;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using FluentAssertions;
using MockQueryable.Moq;
using Moq;
using SharedContracts.Events;
using SharedKernels.Interfaces;

namespace BatteryService.UnitTests.Application;

/// <summary>
/// Sprint 5B #91/#92 — Ambient ingest + history + threshold handlers.
/// </summary>
public class AmbientHandlersTests
{
    /// <summary>Mặc định engine (dedup 30') — ambient dùng chung cấu hình với anomaly battery.</summary>
    private static IOptions<AnomalyEngineOptions> AmbientEngineOptions()
        => Options.Create(new AnomalyEngineOptions());

    /// <summary>
    /// GH-806 — <paramref name="siteIds"/> là các site CÓ THẬT trong DB giả lập.
    /// Handler ingest giờ kiểm site tồn tại trước khi ghi (site lạ ⇒ 404 thay vì lỗi khoá ngoại 500),
    /// nên test nào có ingest đều phải khai site của mình. Đây là yêu cầu mới đúng đắn, không phải
    /// nới lỏng phép kiểm.
    /// </summary>
    private static Mock<IBatteryUnitOfWork> BuildUow(
        List<AmbientReading>? readings = null,
        List<AmbientThresholdConfig>? thresholds = null,
        params Guid[] siteIds)
    {
        readings ??= new List<AmbientReading>();
        thresholds ??= new List<AmbientThresholdConfig>();
        var uow = new Mock<IBatteryUnitOfWork>();

        var sites = siteIds
            .Concat(thresholds.Select(t => t.SiteId))
            .Distinct()
            .Select(id => new Site { Id = id, Name = "Site", Status = SiteStatusEnum.Active })
            .ToList();
        var sitesRepo = new Mock<IGenericRepository<Site>>();
        sitesRepo.Setup(r => r.GetAllAsync()).Returns(() => sites.AsQueryable().BuildMock());
        sitesRepo.Setup(r => r.GetAllAsync(It.IsAny<bool>())).Returns(() => sites.AsQueryable().BuildMock());
        uow.SetupGet(u => u.Sites).Returns(sitesRepo.Object);
        var readingsRepo = new Mock<IGenericRepository<AmbientReading>>();
        var thresholdsRepo = new Mock<IGenericRepository<AmbientThresholdConfig>>();
        readingsRepo.Setup(r => r.GetAllAsync()).Returns(readings.AsQueryable().BuildMock());
        thresholdsRepo.Setup(r => r.GetAllAsync()).Returns(thresholds.AsQueryable().BuildMock());
        uow.SetupGet(u => u.AmbientReadings).Returns(readingsRepo.Object);
        uow.SetupGet(u => u.AmbientThresholdConfigs).Returns(thresholdsRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return uow;
    }

    // Sprint Bonus NS-21 (#661, E1) — uow có Alerts để test detect-at-ingest.
    private static (Mock<IBatteryUnitOfWork> uow, List<Alert> alertsAdded) BuildUowWithAlerts(
        List<AmbientThresholdConfig> thresholds, List<Alert>? existingAlerts = null,
        params Guid[] siteIds)
    {
        var (uow, alertsAdded, _) = BuildUowWithAlertsAndOutbox(thresholds, existingAlerts, siteIds);
        return (uow, alertsAdded);
    }

    /// <summary>
    /// Alert ambient Critical giờ ghi kèm outbox event khởi tạo AlertTicketSaga, nên mock PHẢI có
    /// <c>OutboxMessages</c> — thiếu nó thì handler ném NullReference ở đúng nhánh ta muốn kiểm.
    /// </summary>
    private static (Mock<IBatteryUnitOfWork> uow, List<Alert> alertsAdded, List<OutboxMessage> outbox)
        BuildUowWithAlertsAndOutbox(
            List<AmbientThresholdConfig> thresholds, List<Alert>? existingAlerts = null,
            params Guid[] siteIds)
    {
        var uow = BuildUow(thresholds: thresholds, siteIds: siteIds);
        var alertsAdded = new List<Alert>();
        var alertsRepo = new Mock<IGenericRepository<Alert>>();
        alertsRepo.Setup(r => r.GetAllAsync()).Returns((existingAlerts ?? new List<Alert>()).AsQueryable().BuildMock());
        alertsRepo.Setup(r => r.AddAsync(It.IsAny<Alert>())).Callback<Alert>(alertsAdded.Add).Returns(Task.CompletedTask);
        uow.SetupGet(u => u.Alerts).Returns(alertsRepo.Object);

        var outbox = new List<OutboxMessage>();
        var outboxRepo = new Mock<IGenericRepository<OutboxMessage>>();
        outboxRepo.Setup(r => r.GetAllAsync()).Returns(new List<OutboxMessage>().AsQueryable().BuildMock());
        outboxRepo.Setup(r => r.AddAsync(It.IsAny<OutboxMessage>()))
            .Callback<OutboxMessage>(outbox.Add).Returns(Task.CompletedTask);
        uow.SetupGet(u => u.OutboxMessages).Returns(outboxRepo.Object);

        return (uow, alertsAdded, outbox);
    }

    private static AmbientThresholdConfig Config(Guid siteId, bool enabled = true) => new()
    {
        Id = Guid.NewGuid(),
        SiteId = siteId,
        Enabled = enabled,
        HighAmbientTempWarning = 40m,
        HighAmbientTempCritical = 45m,
        HighHumidityWarning = 85m,
        HighHumidityCritical = 90m,
        ComboTempThreshold = 42m,
        ComboHumidityThreshold = 88m
    };

    // ===== Sprint Bonus NS-21 (#661, E1) — detect-at-ingest ambient anomalies =====

    [Fact]
    public async Task BatchIngest_AmbientOverCritical_CreatesSiteLevelAlert()
    {
        var siteId = Guid.NewGuid();
        var (uow, alerts) = BuildUowWithAlerts(new List<AmbientThresholdConfig> { Config(siteId) });
        var handler = new BatchIngestAmbientReadingsCommandHandler(uow.Object, AmbientEngineOptions());

        await handler.Handle(new BatchIngestAmbientReadingsCommand
        {
            Items = new List<AmbientReadingItem>
            {
                new() { SiteId = siteId, Time = DateTime.UtcNow, AmbientTemperature = 48m, Humidity = 50m }
            }
        }, default);

        alerts.Should().ContainSingle(a =>
            a.AnomalyType == AnomalyTypeEnum.HighAmbientTemp
            && a.Severity == AlertSeverityEnum.Critical
            && a.SiteId == siteId
            && a.BatteryAssetId == null);
    }

    [Fact]
    public async Task BatchIngest_ComboTempHumidity_CreatesComboAlert()
    {
        var siteId = Guid.NewGuid();
        var (uow, alerts) = BuildUowWithAlerts(new List<AmbientThresholdConfig> { Config(siteId) });
        var handler = new BatchIngestAmbientReadingsCommandHandler(uow.Object, AmbientEngineOptions());

        await handler.Handle(new BatchIngestAmbientReadingsCommand
        {
            Items = new List<AmbientReadingItem>
            {
                new() { SiteId = siteId, Time = DateTime.UtcNow, AmbientTemperature = 43m, Humidity = 89m }
            }
        }, default);

        alerts.Should().Contain(a => a.AnomalyType == AnomalyTypeEnum.HighTempHumidityCombo);
    }

    [Fact]
    public async Task BatchIngest_ThresholdDisabled_NoAlert()
    {
        var siteId = Guid.NewGuid();
        var (uow, alerts) = BuildUowWithAlerts(new List<AmbientThresholdConfig> { Config(siteId, enabled: false) });
        var handler = new BatchIngestAmbientReadingsCommandHandler(uow.Object, AmbientEngineOptions());

        await handler.Handle(new BatchIngestAmbientReadingsCommand
        {
            Items = new List<AmbientReadingItem> { new() { SiteId = siteId, Time = DateTime.UtcNow, AmbientTemperature = 48m, Humidity = 95m } }
        }, default);

        alerts.Should().BeEmpty("Enabled=false → không detect (query đã lọc, không load config)");
    }

    [Fact]
    public async Task BatchIngest_NoConfigForSite_SavesReadingsNoAlert()
    {
        var otherSite = Guid.NewGuid();
        var (uow, alerts) = BuildUowWithAlerts(new List<AmbientThresholdConfig>(), siteIds: otherSite); // không có config
        var handler = new BatchIngestAmbientReadingsCommandHandler(uow.Object, AmbientEngineOptions());

        var result = await handler.Handle(new BatchIngestAmbientReadingsCommand
        {
            Items = new List<AmbientReadingItem> { new() { SiteId = otherSite, Time = DateTime.UtcNow, AmbientTemperature = 48m, Humidity = 95m } }
        }, default);

        result.IsSuccess.Should().BeTrue();
        alerts.Should().BeEmpty();
        uow.Verify(u => u.AmbientReadings.AddAsync(It.IsAny<AmbientReading>()), Times.Once, "reading vẫn được lưu");
    }

    /// <summary>
    /// Đổi hành vi có chủ đích: trước đây lần đọc thứ hai bị BỎ HẲN, nên bảng alert im lặng suốt
    /// cửa sổ dedup dù cảm biến vẫn báo vượt ngưỡng. Giờ theo đúng cơ chế của battery — vẫn ghi một
    /// dòng `Merged` trỏ về alert cha để thấy nhịp phần cứng, nhưng KHÔNG mở alert mới và KHÔNG bắn
    /// event (một sự cố vẫn chỉ một ticket).
    /// </summary>
    [Fact]
    public async Task BatchIngest_ExistingOpenAlert_MergesInsteadOfOpeningNewAlert()
    {
        var siteId = Guid.NewGuid();
        var existing = new Alert
        {
            Id = Guid.NewGuid(),
            SiteId = siteId,
            BatteryAssetId = null,
            AnomalyType = AnomalyTypeEnum.HighAmbientTemp,
            Severity = AlertSeverityEnum.Critical,
            DetectedAt = DateTime.UtcNow.AddMinutes(-10),
            Status = AlertStatusEnum.Open,
            DedupWindowEndUtc = DateTime.UtcNow.AddMinutes(50)
        };
        var (uow, alerts, outbox) = BuildUowWithAlertsAndOutbox(
            new List<AmbientThresholdConfig> { Config(siteId) }, new List<Alert> { existing }, siteId);
        var handler = new BatchIngestAmbientReadingsCommandHandler(uow.Object, AmbientEngineOptions());

        await handler.Handle(new BatchIngestAmbientReadingsCommand
        {
            Items = new List<AmbientReadingItem> { new() { SiteId = siteId, Time = DateTime.UtcNow, AmbientTemperature = 48m, Humidity = 50m } }
        }, default);

        var temp = alerts.Where(a => a.AnomalyType == AnomalyTypeEnum.HighAmbientTemp).ToList();
        temp.Should().ContainSingle("vẫn ghi lại nhịp phần cứng thay vì im lặng");
        temp[0].Status.Should().Be(AlertStatusEnum.Merged);
        temp[0].MergedIntoAlertId.Should().Be(existing.Id);
        outbox.Should().BeEmpty("gộp vào sự cố đang mở thì không được đẻ thêm ticket");
    }

    [Fact]
    public async Task BatchIngest_MultipleReadingsSameSite_OpensOneAlertAndMergesTheRest()
    {
        var siteId = Guid.NewGuid();
        var (uow, alerts, outbox) = BuildUowWithAlertsAndOutbox(new List<AmbientThresholdConfig> { Config(siteId) });
        var handler = new BatchIngestAmbientReadingsCommandHandler(uow.Object, AmbientEngineOptions());

        await handler.Handle(new BatchIngestAmbientReadingsCommand
        {
            Items = new List<AmbientReadingItem>
            {
                new() { SiteId = siteId, Time = DateTime.UtcNow, AmbientTemperature = 48m, Humidity = 50m },
                new() { SiteId = siteId, Time = DateTime.UtcNow.AddMinutes(-1), AmbientTemperature = 49m, Humidity = 50m }
            }
        }, default);

        var temp = alerts.Where(a => a.AnomalyType == AnomalyTypeEnum.HighAmbientTemp).ToList();
        temp.Should().HaveCount(2, "mỗi lần đọc vượt ngưỡng đều để lại dấu vết");
        temp.Count(a => a.Status == AlertStatusEnum.Open).Should().Be(1, "chỉ một sự cố được mở");
        temp.Count(a => a.Status == AlertStatusEnum.Merged).Should().Be(1);
        outbox.Count(m => m.Type == nameof(BatteryAnomalyDetectedV2Event)).Should().Be(1,
            "một sự cố chỉ khởi tạo một saga → một ticket");
    }

    // ===== Batch ingest =====

    [Fact]
    public async Task BatchIngest_ValidItems_ShouldSave()
    {
        var siteId = Guid.NewGuid();
        var uow = BuildUow(siteIds: siteId);
        var handler = new BatchIngestAmbientReadingsCommandHandler(uow.Object, AmbientEngineOptions());

        var result = await handler.Handle(new BatchIngestAmbientReadingsCommand
        {
            Items = new List<AmbientReadingItem>
            {
                new() { SiteId = siteId, Time = DateTime.UtcNow, AmbientTemperature = 30m, Humidity = 65m },
                new() { SiteId = siteId, Time = DateTime.UtcNow.AddMinutes(-1), AmbientTemperature = 29m, Humidity = 64m }
            }
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().Be(2);
        uow.Verify(u => u.AmbientReadings.AddAsync(It.IsAny<AmbientReading>()), Times.Exactly(2));
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BatchIngest_Validation_TooManyItems_ShouldFail()
    {
        var cmd = new BatchIngestAmbientReadingsCommand
        {
            Items = Enumerable.Range(0, 101)
                .Select(_ => new AmbientReadingItem
                {
                    SiteId = Guid.NewGuid(),
                    Time = DateTime.UtcNow,
                    AmbientTemperature = 25m,
                    Humidity = 50m
                }).ToList()
        };

        var result = await cmd.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task BatchIngest_Validation_HumidityOutOfRange_ShouldFail()
    {
        var cmd = new BatchIngestAmbientReadingsCommand
        {
            Items = new List<AmbientReadingItem>
            {
                new() { SiteId = Guid.NewGuid(), Time = DateTime.UtcNow,
                        AmbientTemperature = 25m, Humidity = 105m }
            }
        };

        var result = await cmd.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(e => e.Detail.Contains("Humidity"));
    }

    // ===== History query =====

    [Fact]
    public async Task GetHistory_ShouldReturnPaginatedSortedDesc()
    {
        var siteId = Guid.NewGuid();
        var older = new AmbientReading { Time = DateTime.UtcNow.AddHours(-1), SiteId = siteId, AmbientTemperature = 28m, Humidity = 60m, Source = AmbientReadingSourceEnum.IotSensor };
        var newer = new AmbientReading { Time = DateTime.UtcNow, SiteId = siteId, AmbientTemperature = 30m, Humidity = 70m, Source = AmbientReadingSourceEnum.IotSensor };
        var uow = BuildUow(readings: new List<AmbientReading> { older, newer });

        var handler = new GetAmbientReadingHistoryQueryHandler(uow.Object);
        var result = await handler.Handle(new GetAmbientReadingHistoryQuery { SiteId = siteId, PageSize = 10 }, default);

        result.Data!.Items.Should().HaveCount(2);
        result.Data.Items[0].AmbientTemperature.Should().Be(30m); // newest first
    }

    [Fact]
    public async Task GetLatest_NoData_ShouldReturn404()
    {
        var uow = BuildUow();
        var handler = new GetLatestAmbientReadingQueryHandler(uow.Object);

        var result = await handler.Handle(new GetLatestAmbientReadingQuery { SiteId = Guid.NewGuid() }, default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetLatest_WithData_ShouldReturnMostRecent()
    {
        var siteId = Guid.NewGuid();
        var older = new AmbientReading { Time = DateTime.UtcNow.AddHours(-1), SiteId = siteId, AmbientTemperature = 28m, Humidity = 60m, Source = AmbientReadingSourceEnum.IotSensor };
        var newer = new AmbientReading { Time = DateTime.UtcNow, SiteId = siteId, AmbientTemperature = 33m, Humidity = 70m, Source = AmbientReadingSourceEnum.IotSensor };
        var uow = BuildUow(readings: new List<AmbientReading> { older, newer });

        var handler = new GetLatestAmbientReadingQueryHandler(uow.Object);
        var result = await handler.Handle(new GetLatestAmbientReadingQuery { SiteId = siteId }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data!.AmbientTemperature.Should().Be(33m);
    }

    // ===== Threshold upsert =====

    [Fact]
    public async Task Upsert_CreateNew_WhenNotExist()
    {
        var siteId = Guid.NewGuid();
        var uow = BuildUow(thresholds: new List<AmbientThresholdConfig>());

        var handler = new UpsertAmbientThresholdConfigCommandHandler(uow.Object);
        var result = await handler.Handle(new UpsertAmbientThresholdConfigCommand
        {
            SiteId = siteId,
            HighAmbientTempWarning = 35m,
            HighAmbientTempCritical = 40m,
            Enabled = true
        }, default);

        result.IsSuccess.Should().BeTrue();
        uow.Verify(u => u.AmbientThresholdConfigs.AddAsync(It.IsAny<AmbientThresholdConfig>()), Times.Once);
        uow.Verify(u => u.AmbientThresholdConfigs.UpdateAsync(It.IsAny<AmbientThresholdConfig>()), Times.Never);
    }

    [Fact]
    public async Task Upsert_UpdateExisting_WhenAlreadyExists()
    {
        var siteId = Guid.NewGuid();
        var existing = new AmbientThresholdConfig
        {
            Id = Guid.NewGuid(),
            SiteId = siteId,
            HighAmbientTempWarning = 30m,
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        var uow = BuildUow(thresholds: new List<AmbientThresholdConfig> { existing });

        var handler = new UpsertAmbientThresholdConfigCommandHandler(uow.Object);
        var result = await handler.Handle(new UpsertAmbientThresholdConfigCommand
        {
            SiteId = siteId,
            HighAmbientTempWarning = 38m,
            HighAmbientTempCritical = 42m
        }, default);

        result.IsSuccess.Should().BeTrue();
        existing.HighAmbientTempWarning.Should().Be(38m);
        uow.Verify(u => u.AmbientThresholdConfigs.AddAsync(It.IsAny<AmbientThresholdConfig>()), Times.Never);
        uow.Verify(u => u.AmbientThresholdConfigs.UpdateAsync(It.IsAny<AmbientThresholdConfig>()), Times.Once);
    }

    [Fact]
    public async Task Upsert_Validation_CriticalLowerThanWarning_ShouldFail()
    {
        var cmd = new UpsertAmbientThresholdConfigCommand
        {
            SiteId = Guid.NewGuid(),
            HighAmbientTempWarning = 40m,
            HighAmbientTempCritical = 35m
        };

        var result = await cmd.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task GetThresholdBySite_NotConfigured_ShouldReturn200WithNullData()
    {
        var uow = BuildUow(thresholds: new List<AmbientThresholdConfig>());
        var handler = new GetAmbientThresholdBySiteQueryHandler(uow.Object);

        var result = await handler.Handle(new GetAmbientThresholdBySiteQuery { SiteId = Guid.NewGuid() }, default);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.Data.Should().BeNull();
    }

    [Fact]
    public async Task ListThresholds_ShouldReturnPaginated()
    {
        var thresholds = new List<AmbientThresholdConfig>
        {
            new() { Id = Guid.NewGuid(), SiteId = Guid.NewGuid(), Enabled = true, CreatedAt = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), SiteId = Guid.NewGuid(), Enabled = true, CreatedAt = DateTime.UtcNow.AddDays(-1) }
        };
        var uow = BuildUow(thresholds: thresholds);

        var handler = new ListAmbientThresholdsQueryHandler(uow.Object);
        var result = await handler.Handle(new ListAmbientThresholdsQuery { PageSize = 10 }, default);

        result.Data!.TotalItems.Should().Be(2);
        result.Data.Items.Should().HaveCount(2);
    }
}
