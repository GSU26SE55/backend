using BatteryService.Application.CQRS.Handler.Report;
using BatteryService.Application.CQRS.Query.Report;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using FluentAssertions;
using MockQueryable.Moq;
using Moq;
using SharedKernels.Interfaces;

namespace BatteryService.UnitTests.Application;

/// <summary>Sprint 7 #114 (§5.2) — BatteryService report handlers.</summary>
public class BatteryReportHandlersTests
{
    private static Mock<IGenericRepository<T>> Repo<T>(List<T> data) where T : class
    {
        var repo = new Mock<IGenericRepository<T>>();
        repo.Setup(r => r.GetAllAsync()).Returns(data.AsQueryable().BuildMock());
        return repo;
    }

    [Fact]
    public async Task BatteryHealthByType_Computes_HealthScore()
    {
        var typeId = Guid.NewGuid();
        var a1 = Guid.NewGuid();
        var a2 = Guid.NewGuid();

        var types = new List<BatteryType> { new() { Id = typeId, Name = "LiFePO4-100Ah" } };
        var assets = new List<BatteryAsset>
        {
            new() { Id = a1, BatteryTypeId = typeId, SerialNumber = "S1", CustomerId = Guid.NewGuid() },
            new() { Id = a2, BatteryTypeId = typeId, SerialNumber = "S2", CustomerId = Guid.NewGuid() }
        };
        var alerts = new List<Alert>
        {
            new() { Id = Guid.NewGuid(), BatteryAssetId = a1, Status = AlertStatusEnum.Open, DetectedAt = DateTime.UtcNow }
        };

        var uow = new Mock<IBatteryUnitOfWork>();
        uow.SetupGet(u => u.BatteryTypes).Returns(Repo(types).Object);
        uow.SetupGet(u => u.BatteryAssets).Returns(Repo(assets).Object);
        uow.SetupGet(u => u.Alerts).Returns(Repo(alerts).Object);

        var res = await new BatteryHealthByTypeReportQueryHandler(uow.Object)
            .Handle(new BatteryHealthByTypeReportQuery(), default);

        res.IsSuccess.Should().BeTrue();
        var row = res.Data!.Single();
        row.TotalAssets.Should().Be(2);
        row.WithActiveAlerts.Should().Be(1);
        row.HealthScore.Should().Be(50.0m);   // 1/2 không có alert
    }

    [Fact]
    public async Task TopAnomalies_Orders_By_Count_With_CriticalCount()
    {
        var asset = Guid.NewGuid();
        var alerts = new List<Alert>
        {
            new() { Id = Guid.NewGuid(), BatteryAssetId = asset, AnomalyType = AnomalyTypeEnum.Overheat, Severity = AlertSeverityEnum.Critical, Status = AlertStatusEnum.Open, DetectedAt = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), BatteryAssetId = asset, AnomalyType = AnomalyTypeEnum.Overheat, Severity = AlertSeverityEnum.Warning, Status = AlertStatusEnum.Open, DetectedAt = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), BatteryAssetId = asset, AnomalyType = AnomalyTypeEnum.LowSoc, Severity = AlertSeverityEnum.Warning, Status = AlertStatusEnum.Open, DetectedAt = DateTime.UtcNow }
        };

        var uow = new Mock<IBatteryUnitOfWork>();
        uow.SetupGet(u => u.Alerts).Returns(Repo(alerts).Object);

        var res = await new TopAnomaliesReportQueryHandler(uow.Object)
            .Handle(new TopAnomaliesReportQuery { Limit = 10 }, default);

        res.Data!.First().AnomalyType.Should().Be("Overheat");
        res.Data!.First().Count.Should().Be(2);
        res.Data!.First().CriticalCount.Should().Be(1);
    }

    [Fact]
    public async Task EnvironmentalIncidents_Computes_Duration_And_FalseAlarm()
    {
        var detected = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var incidents = new List<EnvironmentalIncident>
        {
            new()
            {
                Id = Guid.NewGuid(), SiteId = Guid.NewGuid(),
                IncidentType = EnvironmentalIncidentTypeEnum.Smoke,
                Severity = AlertSeverityEnum.Critical,
                DetectedAt = detected, ResolvedAt = detected.AddHours(2.5),
                FalseAlarmAt = DateTime.UtcNow
            }
        };

        var uow = new Mock<IBatteryUnitOfWork>();
        uow.SetupGet(u => u.EnvironmentalIncidents).Returns(Repo(incidents).Object);

        var res = await new EnvironmentalIncidentsReportQueryHandler(uow.Object)
            .Handle(new EnvironmentalIncidentsReportQuery(), default);

        var row = res.Data!.Single();
        row.DurationHours.Should().Be(2.5m);
        row.WasFalseAlarm.Should().BeTrue();
        row.IncidentType.Should().Be("Smoke");
    }
}
