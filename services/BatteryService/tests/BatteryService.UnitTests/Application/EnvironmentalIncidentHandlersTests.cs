using BatteryService.Application.CQRS.Command.EnvironmentalIncident;
using BatteryService.Application.CQRS.Handler.EnvironmentalIncident;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using FluentAssertions;
using MockQueryable.Moq;
using Moq;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using SharedKernels.Interfaces;
using IncidentEntity = BatteryService.Domain.Entities.EnvironmentalIncident;

namespace BatteryService.UnitTests.Application;

/// <summary>
/// Sprint 5B #102/#104 — EnvironmentalIncident lifecycle handlers.
/// </summary>
public class EnvironmentalIncidentHandlersTests
{
    private static (Mock<IBatteryUnitOfWork> Uow,
                    Mock<IGenericRepository<IncidentEntity>> IncidentRepo,
                    Mock<IGenericRepository<Alert>> AlertRepo)
        BuildUow(List<IncidentEntity> incidents, List<Alert>? alerts = null,
                 List<BatteryService.Domain.Entities.Site>? sites = null)
    {
        alerts ??= new List<Alert>();
        sites ??= new List<BatteryService.Domain.Entities.Site>();
        var uow = new Mock<IBatteryUnitOfWork>();
        var incRepo = new Mock<IGenericRepository<IncidentEntity>>();
        var aleRepo = new Mock<IGenericRepository<Alert>>();
        var siteRepo = new Mock<IGenericRepository<BatteryService.Domain.Entities.Site>>();
        incRepo.Setup(r => r.GetAllAsync()).Returns(incidents.AsQueryable().BuildMock());
        aleRepo.Setup(r => r.GetAllAsync()).Returns(alerts.AsQueryable().BuildMock());
        siteRepo.Setup(r => r.GetAllAsync()).Returns(sites.AsQueryable().BuildMock());
        uow.SetupGet(u => u.EnvironmentalIncidents).Returns(incRepo.Object);
        uow.SetupGet(u => u.Alerts).Returns(aleRepo.Object);
        uow.SetupGet(u => u.Sites).Returns(siteRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return (uow, incRepo, aleRepo);
    }

    // ===== Report =====

    [Fact]
    public async Task Report_NoActiveIncident_ShouldCreateNewIncidentAlertAndPublishEvent()
    {
        var (uow, incRepo, aleRepo) = BuildUow(new List<IncidentEntity>());
        var outbox = new Mock<IIntegrationEventOutboxWriter>();

        var handler = new ReportEnvironmentalIncidentCommandHandler(uow.Object, outbox.Object);
        var result = await handler.Handle(new ReportEnvironmentalIncidentCommand
        {
            SiteId = Guid.NewGuid(),
            IncidentType = EnvironmentalIncidentTypeEnum.Smoke,
            Severity = AlertSeverityEnum.Critical,
            DetectedAt = DateTime.UtcNow.AddMinutes(-1),
            ReportedBy = "iot-1",
            Notes = "smoke detected"
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(201);
        result.Data!.Status.Should().Be(EnvironmentalIncidentStatusEnum.Open);

        incRepo.Verify(r => r.AddAsync(It.IsAny<IncidentEntity>()), Times.Once);
        aleRepo.Verify(r => r.AddAsync(It.Is<Alert>(a =>
            a.AnomalyType == AnomalyTypeEnum.EnvironmentalIncident &&
            a.SiteId != null && a.BatteryAssetId == null)), Times.Once);
        outbox.Verify(o => o.WriteAsync(It.IsAny<EnvironmentalIncidentDetectedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Report_DuplicateActiveIncident_ShouldReuseExisting_NotCreateNew()
    {
        var siteId = Guid.NewGuid();
        var existing = new IncidentEntity
        {
            Id = Guid.NewGuid(),
            SiteId = siteId,
            IncidentType = EnvironmentalIncidentTypeEnum.Smoke,
            Status = EnvironmentalIncidentStatusEnum.Open,
            Severity = AlertSeverityEnum.Critical,
            DetectedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        var (uow, incRepo, aleRepo) = BuildUow(new List<IncidentEntity> { existing });
        var outbox = new Mock<IIntegrationEventOutboxWriter>();

        var handler = new ReportEnvironmentalIncidentCommandHandler(uow.Object, outbox.Object);
        var result = await handler.Handle(new ReportEnvironmentalIncidentCommand
        {
            SiteId = siteId,
            IncidentType = EnvironmentalIncidentTypeEnum.Smoke,
            DetectedAt = DateTime.UtcNow
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Id.Should().Be(existing.Id.ToString());
        incRepo.Verify(r => r.AddAsync(It.IsAny<IncidentEntity>()), Times.Never);
        aleRepo.Verify(r => r.AddAsync(It.IsAny<Alert>()), Times.Never);
        outbox.Verify(o => o.WriteAsync(It.IsAny<EnvironmentalIncidentDetectedEvent>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    // ===== Acknowledge =====

    [Fact]
    public async Task Acknowledge_OpenIncident_ShouldTransitionToAcknowledged()
    {
        var incident = new IncidentEntity
        {
            Id = Guid.NewGuid(),
            SiteId = Guid.NewGuid(),
            IncidentType = EnvironmentalIncidentTypeEnum.Smoke,
            Status = EnvironmentalIncidentStatusEnum.Open,
            Severity = AlertSeverityEnum.Critical,
            DetectedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        var (uow, incRepo, _) = BuildUow(new List<IncidentEntity> { incident });
        incRepo.Setup(r => r.GetByIdAsync(incident.Id)).ReturnsAsync(incident);

        var handler = new AcknowledgeEnvironmentalIncidentCommandHandler(uow.Object);
        var actor = Guid.NewGuid();
        var result = await handler.Handle(new AcknowledgeEnvironmentalIncidentCommand
        {
            Id = incident.Id,
            AcknowledgedBy = actor
        }, default);

        result.IsSuccess.Should().BeTrue();
        incident.Status.Should().Be(EnvironmentalIncidentStatusEnum.Acknowledged);
        incident.AcknowledgedBy.Should().Be(actor);
    }

    [Fact]
    public async Task Acknowledge_AlreadyResolved_ShouldReturn409()
    {
        var incident = new IncidentEntity
        {
            Id = Guid.NewGuid(),
            SiteId = Guid.NewGuid(),
            Status = EnvironmentalIncidentStatusEnum.Resolved,
            CreatedAt = DateTime.UtcNow,
            DetectedAt = DateTime.UtcNow
        };
        var (uow, incRepo, _) = BuildUow(new List<IncidentEntity> { incident });
        incRepo.Setup(r => r.GetByIdAsync(incident.Id)).ReturnsAsync(incident);

        var handler = new AcknowledgeEnvironmentalIncidentCommandHandler(uow.Object);
        var result = await handler.Handle(new AcknowledgeEnvironmentalIncidentCommand
        {
            Id = incident.Id,
            AcknowledgedBy = Guid.NewGuid()
        }, default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Acknowledge_NotFound_ShouldReturn404()
    {
        var (uow, incRepo, _) = BuildUow(new List<IncidentEntity>());
        incRepo.Setup(r => r.GetByIdAsync(It.IsAny<object>())).ReturnsAsync((IncidentEntity?)null);

        var handler = new AcknowledgeEnvironmentalIncidentCommandHandler(uow.Object);
        var result = await handler.Handle(new AcknowledgeEnvironmentalIncidentCommand
        {
            Id = Guid.NewGuid(),
            AcknowledgedBy = Guid.NewGuid()
        }, default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    // ===== Resolve =====

    [Fact]
    public async Task Resolve_OpenIncidentWithAlerts_ShouldCloseBothAndPublishResolvedEvent()
    {
        var siteId = Guid.NewGuid();
        var incident = new IncidentEntity
        {
            Id = Guid.NewGuid(),
            SiteId = siteId,
            Status = EnvironmentalIncidentStatusEnum.Open,
            Severity = AlertSeverityEnum.Critical,
            DetectedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            Alerts = new List<Alert>
            {
                new()
                {
                    Id = Guid.NewGuid(), SiteId = siteId,
                    AnomalyType = AnomalyTypeEnum.EnvironmentalIncident,
                    Severity = AlertSeverityEnum.Critical,
                    ThresholdValue = 0, ActualValue = 0, Unit = "incident",
                    DetectedAt = DateTime.UtcNow,
                    Status = AlertStatusEnum.Open,
                    DedupWindowEndUtc = DateTime.UtcNow.AddHours(1),
                    CreatedAt = DateTime.UtcNow
                }
            }
        };
        var (uow, _, _) = BuildUow(new List<IncidentEntity> { incident });
        var outbox = new Mock<IIntegrationEventOutboxWriter>();

        var handler = new ResolveEnvironmentalIncidentCommandHandler(uow.Object, outbox.Object);
        var result = await handler.Handle(new ResolveEnvironmentalIncidentCommand
        {
            Id = incident.Id,
            ResolvedBy = Guid.NewGuid(),
            ResolutionNote = "ventilated"
        }, default);

        result.IsSuccess.Should().BeTrue();
        incident.Status.Should().Be(EnvironmentalIncidentStatusEnum.Resolved);
        incident.Alerts.First().Status.Should().Be(AlertStatusEnum.Resolved);
        outbox.Verify(o => o.WriteAsync(It.Is<EnvironmentalIncidentResolvedEvent>(
            e => e.WasFalseAlarm == false), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ===== False Alarm =====

    [Fact]
    public async Task MarkFalseAlarm_OpenIncident_ShouldClosingAlertsAndPublishFalseAlarmEvent()
    {
        var siteId = Guid.NewGuid();
        var incident = new IncidentEntity
        {
            Id = Guid.NewGuid(),
            SiteId = siteId,
            Status = EnvironmentalIncidentStatusEnum.Open,
            Severity = AlertSeverityEnum.Critical,
            DetectedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            Alerts = new List<Alert>
            {
                new()
                {
                    Id = Guid.NewGuid(), SiteId = siteId,
                    AnomalyType = AnomalyTypeEnum.EnvironmentalIncident,
                    Severity = AlertSeverityEnum.Critical,
                    ThresholdValue = 0, ActualValue = 0, Unit = "incident",
                    DetectedAt = DateTime.UtcNow,
                    Status = AlertStatusEnum.Open,
                    DedupWindowEndUtc = DateTime.UtcNow.AddHours(1),
                    CreatedAt = DateTime.UtcNow
                }
            }
        };
        var (uow, _, _) = BuildUow(new List<IncidentEntity> { incident });
        var outbox = new Mock<IIntegrationEventOutboxWriter>();

        var handler = new MarkFalseAlarmEnvironmentalIncidentCommandHandler(uow.Object, outbox.Object);
        var result = await handler.Handle(new MarkFalseAlarmEnvironmentalIncidentCommand
        {
            Id = incident.Id,
            FalseAlarmBy = Guid.NewGuid(),
            FalseAlarmReason = "dust"
        }, default);

        result.IsSuccess.Should().BeTrue();
        incident.Status.Should().Be(EnvironmentalIncidentStatusEnum.FalseAlarm);
        incident.Alerts.First().Status.Should().Be(AlertStatusEnum.Resolved);
        outbox.Verify(o => o.WriteAsync(It.Is<EnvironmentalIncidentResolvedEvent>(
            e => e.WasFalseAlarm == true), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ===== Validation =====

    [Fact]
    public async Task Validation_EmptySiteId_ShouldFail()
    {
        var cmd = new ReportEnvironmentalIncidentCommand
        {
            SiteId = Guid.Empty,
            IncidentType = EnvironmentalIncidentTypeEnum.Smoke,
            DetectedAt = DateTime.UtcNow
        };

        var result = await cmd.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(e => e.Field == nameof(cmd.SiteId));
    }

    [Fact]
    public async Task Validation_FutureDetectedAt_ShouldFail()
    {
        var cmd = new ReportEnvironmentalIncidentCommand
        {
            SiteId = Guid.NewGuid(),
            IncidentType = EnvironmentalIncidentTypeEnum.Smoke,
            DetectedAt = DateTime.UtcNow.AddHours(1)
        };

        var result = await cmd.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
    }
}
