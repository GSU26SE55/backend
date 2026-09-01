using BatteryService.Application.Anomaly;
using BatteryService.Application.CQRS.Command.Ambient;
using BatteryService.Application.CQRS.Command.EnvironmentalIncident;
using BatteryService.Application.CQRS.Handler.Ambient;
using BatteryService.Application.CQRS.Handler.EnvironmentalIncident;
using BatteryService.Application.CQRS.Query.Ambient;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Services;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.Infrastructure.Implements.Repositories;
using BatteryService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Moq;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using SharedInfrastructure.Persistence.Interceptors;
using SharedInfrastructure.Services;

namespace BatteryService.IntegrationTests.Application;

/// <summary>
/// Sprint 5B #93/#105 — End-to-end flow integration tests dùng InMemory DB.
///
/// Cases:
/// 1. Ambient ingest → query latest → trả về reading đã ingest.
/// 2. Ambient combo threshold breach → AnomalyRules.DetectAmbient raise Combo Critical.
/// 3. ReportEnvironmentalIncident (smoke) → Alert critical site-level + EnvironmentalIncidentDetectedEvent
///    → MarkFalseAlarm → close cả incident + alert.
/// </summary>
public class Sprint5BAmbientFlowIntegrationTests
{
    private static readonly Guid CustomerId = Guid.NewGuid();
    private static readonly Guid SiteId = Guid.NewGuid();

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"sprint5b-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var http = new HttpContextAccessor();
        var interceptor = new AuditableEntityInterceptor(new CurrentUserService(http));
        return new ApplicationDbContext(options, interceptor);
    }

    private static async Task<Site> SeedSiteAsync(ApplicationDbContext db)
    {
        var site = new Site
        {
            Id = SiteId,
            Name = "Solar Farm Test",
            CustomerId = CustomerId,
            Address = "Test",
            Latitude = 10m,
            Longitude = 106m,
            InstallDate = DateTime.UtcNow.AddYears(-1),
            Status = SiteStatusEnum.Active,
            CreatedAt = DateTime.UtcNow
        };
        db.Sites.Add(site);
        await db.SaveChangesAsync();
        return site;
    }

    [Fact]
    public async Task IngestAmbient_AndQueryLatest_ShouldRoundTrip()
    {
        await using var db = CreateDbContext();
        await SeedSiteAsync(db);

        var ingest = new BatchIngestAmbientReadingsCommandHandler(
            new UnitOfWork(db),
            Microsoft.Extensions.Options.Options.Create(new BatteryService.Application.Anomaly.AnomalyEngineOptions()),
            new OutboxSignal());
        var ingestTime = DateTime.UtcNow;
        var ingestResult = await ingest.Handle(new BatchIngestAmbientReadingsCommand
        {
            Items = new List<AmbientReadingItem>
            {
                new() { SiteId = SiteId, Time = ingestTime, AmbientTemperature = 38m, Humidity = 88m, Source = AmbientReadingSourceEnum.IotSensor }
            }
        }, default);

        ingestResult.IsSuccess.Should().BeTrue();
        ingestResult.Data.Should().Be(1);

        var query = new GetLatestAmbientReadingQueryHandler(new UnitOfWork(db));
        var latest = await query.Handle(new GetLatestAmbientReadingQuery { SiteId = SiteId }, default);

        latest.IsSuccess.Should().BeTrue();
        latest.Data!.AmbientTemperature.Should().Be(38m);
        latest.Data.Humidity.Should().Be(88m);
    }

    [Fact]
    public async Task ComboThresholdBreach_ShouldDetectComboAnomaly()
    {
        await using var db = CreateDbContext();
        await SeedSiteAsync(db);

        var reading = new AmbientReading
        {
            Time = DateTime.UtcNow,
            SiteId = SiteId,
            AmbientTemperature = 38m,
            Humidity = 88m,
            Source = AmbientReadingSourceEnum.IotSensor
        };
        var threshold = new AmbientThresholdConfig
        {
            Id = Guid.NewGuid(),
            SiteId = SiteId,
            HighAmbientTempWarning = 30m,
            HighAmbientTempCritical = 40m,
            HighHumidityWarning = 75m,
            HighHumidityCritical = 90m,
            ComboTempThreshold = 35m,
            ComboHumidityThreshold = 80m,
            Enabled = true
        };

        var anomalies = AnomalyRules.DetectAmbient(reading, threshold);

        anomalies.Should().Contain(a => a.Type == AnomalyTypeEnum.HighTempHumidityCombo);
        anomalies.Should().Contain(a => a.Type == AnomalyTypeEnum.HighAmbientTemp); // 38 > warning
        anomalies.Should().Contain(a => a.Type == AnomalyTypeEnum.HighHumidity);    // 88 > warning
    }

    [Fact]
    public async Task SmokeIncident_ReportThenFalseAlarm_ShouldCloseBoth()
    {
        await using var db = CreateDbContext();
        await SeedSiteAsync(db);

        var outboxMessages = new List<(string Type, object Event)>();
        var outbox = new Mock<IIntegrationEventOutboxWriter>();
        outbox.Setup(o => o.WriteAsync(It.IsAny<EnvironmentalIncidentDetectedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<EnvironmentalIncidentDetectedEvent, CancellationToken>((e, _) =>
                outboxMessages.Add((nameof(EnvironmentalIncidentDetectedEvent), e)))
            .Returns(Task.CompletedTask);
        outbox.Setup(o => o.WriteAsync(It.IsAny<EnvironmentalIncidentResolvedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<EnvironmentalIncidentResolvedEvent, CancellationToken>((e, _) =>
                outboxMessages.Add((nameof(EnvironmentalIncidentResolvedEvent), e)))
            .Returns(Task.CompletedTask);

        var report = new ReportEnvironmentalIncidentCommandHandler(new UnitOfWork(db), outbox.Object, new Mock<IEnvironmentalMetricsRecorder>().Object);
        var reportResult = await report.Handle(new ReportEnvironmentalIncidentCommand
        {
            SiteId = SiteId,
            IncidentType = EnvironmentalIncidentTypeEnum.Smoke,
            Severity = AlertSeverityEnum.Critical,
            DetectedAt = DateTime.UtcNow,
            ReportedBy = "iot-1",
            Notes = "smoke detected"
        }, default);

        reportResult.IsSuccess.Should().BeTrue();
        var incidentId = Guid.Parse(reportResult.Data!.Id);

        // Verify: incident + alert + event đã được tạo
        var incident = await db.EnvironmentalIncidents.SingleAsync(i => i.Id == incidentId);
        incident.Status.Should().Be(EnvironmentalIncidentStatusEnum.Open);

        var alert = await db.Alerts.SingleAsync(a => a.EnvironmentalIncidentId == incidentId);
        alert.Status.Should().Be(AlertStatusEnum.Open);
        alert.SiteId.Should().Be(SiteId);
        alert.BatteryAssetId.Should().BeNull();
        alert.AnomalyType.Should().Be(AnomalyTypeEnum.EnvironmentalIncident);

        outboxMessages.Should().ContainSingle(m => m.Type == nameof(EnvironmentalIncidentDetectedEvent));

        // Mark FalseAlarm — close cả incident + alert
        var falseAlarm = new MarkFalseAlarmEnvironmentalIncidentCommandHandler(new UnitOfWork(db), outbox.Object);
        var faResult = await falseAlarm.Handle(new MarkFalseAlarmEnvironmentalIncidentCommand
        {
            Id = incidentId,
            FalseAlarmBy = Guid.NewGuid(),
            FalseAlarmReason = "dust trigger"
        }, default);

        faResult.IsSuccess.Should().BeTrue();

        var incidentReload = await db.EnvironmentalIncidents.AsNoTracking().SingleAsync(i => i.Id == incidentId);
        incidentReload.Status.Should().Be(EnvironmentalIncidentStatusEnum.FalseAlarm);

        var alertReload = await db.Alerts.AsNoTracking().SingleAsync(a => a.Id == alert.Id);
        alertReload.Status.Should().Be(AlertStatusEnum.Resolved);

        outboxMessages.Should().Contain(m =>
            m.Type == nameof(EnvironmentalIncidentResolvedEvent)
            && ((EnvironmentalIncidentResolvedEvent)m.Event).WasFalseAlarm == true);
    }

    [Fact]
    public async Task SmokeIncident_ReportThenResolve_ShouldCloseBoth()
    {
        await using var db = CreateDbContext();
        await SeedSiteAsync(db);

        var outbox = new Mock<IIntegrationEventOutboxWriter>();
        outbox.Setup(o => o.WriteAsync(It.IsAny<EnvironmentalIncidentDetectedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        outbox.Setup(o => o.WriteAsync(It.IsAny<EnvironmentalIncidentResolvedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var report = new ReportEnvironmentalIncidentCommandHandler(new UnitOfWork(db), outbox.Object, new Mock<IEnvironmentalMetricsRecorder>().Object);
        var reportResult = await report.Handle(new ReportEnvironmentalIncidentCommand
        {
            SiteId = SiteId,
            IncidentType = EnvironmentalIncidentTypeEnum.Smoke,
            DetectedAt = DateTime.UtcNow
        }, default);

        var incidentId = Guid.Parse(reportResult.Data!.Id);

        var resolve = new ResolveEnvironmentalIncidentCommandHandler(new UnitOfWork(db), outbox.Object);
        var resolveResult = await resolve.Handle(new ResolveEnvironmentalIncidentCommand
        {
            Id = incidentId,
            ResolvedBy = Guid.NewGuid(),
            ResolutionNote = "ventilated"
        }, default);

        resolveResult.IsSuccess.Should().BeTrue();

        var incidentReload = await db.EnvironmentalIncidents.AsNoTracking().SingleAsync(i => i.Id == incidentId);
        incidentReload.Status.Should().Be(EnvironmentalIncidentStatusEnum.Resolved);

        var alerts = await db.Alerts.AsNoTracking().Where(a => a.EnvironmentalIncidentId == incidentId).ToListAsync();
        alerts.Should().AllSatisfy(a => a.Status.Should().Be(AlertStatusEnum.Resolved));

        outbox.Verify(o => o.WriteAsync(It.Is<EnvironmentalIncidentResolvedEvent>(
            e => e.WasFalseAlarm == false), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DuplicateSmokeReport_ShouldNotCreateSecondIncident()
    {
        await using var db = CreateDbContext();
        await SeedSiteAsync(db);

        var outbox = new Mock<IIntegrationEventOutboxWriter>();
        outbox.Setup(o => o.WriteAsync(It.IsAny<EnvironmentalIncidentDetectedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var report = new ReportEnvironmentalIncidentCommandHandler(new UnitOfWork(db), outbox.Object, new Mock<IEnvironmentalMetricsRecorder>().Object);
        var cmd = new ReportEnvironmentalIncidentCommand
        {
            SiteId = SiteId,
            IncidentType = EnvironmentalIncidentTypeEnum.Smoke,
            DetectedAt = DateTime.UtcNow
        };
        var first = await report.Handle(cmd, default);
        var second = await report.Handle(cmd, default);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        // Cùng SiteId+Type+Status=Open → reuse existing
        second.Data!.Id.Should().Be(first.Data!.Id);

        var count = await db.EnvironmentalIncidents.CountAsync();
        count.Should().Be(1);
    }
}
