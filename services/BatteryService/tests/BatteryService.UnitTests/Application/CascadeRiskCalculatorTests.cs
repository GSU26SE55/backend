using BatteryService.Application.Interfaces;
using BatteryService.Application.Services;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using FluentAssertions;
using MockQueryable.Moq;
using Moq;
using SharedKernels.Interfaces;

namespace BatteryService.UnitTests.Application;

/// <summary>
/// Sprint 7 B4 (§31.7) — unit test cho CascadeRiskCalculator (rule-based).
/// Proximity adapt theo SiteId (BatteryGroup đã bị remove).
/// </summary>
public class CascadeRiskCalculatorTests
{
    private static CascadeRiskCalculator Build(
        BatteryAsset target, List<BatteryAsset> allAssets, List<Alert> alerts)
    {
        var uow = new Mock<IBatteryUnitOfWork>();

        var assetRepo = new Mock<IGenericRepository<BatteryAsset>>();
        assetRepo.Setup(r => r.GetByIdAsync(It.IsAny<object>()))
            .ReturnsAsync((object id) => allAssets.FirstOrDefault(a => a.Id == (Guid)id));
        assetRepo.Setup(r => r.GetAllAsync()).Returns(allAssets.AsQueryable().BuildMock());
        uow.SetupGet(u => u.BatteryAssets).Returns(assetRepo.Object);

        var alertRepo = new Mock<IGenericRepository<Alert>>();
        alertRepo.Setup(r => r.GetAllAsync()).Returns(alerts.AsQueryable().BuildMock());
        uow.SetupGet(u => u.Alerts).Returns(alertRepo.Object);

        return new CascadeRiskCalculator(uow.Object);
    }

    private static BatteryAsset Asset(Guid id, ElectricalTopologyEnum topology, Guid? siteId = null) => new()
    {
        Id = id,
        SerialNumber = $"SN-{id.ToString()[..8]}",
        ElectricalTopology = topology,
        SiteId = siteId,
        CustomerId = Guid.NewGuid()
    };

    private static Alert OpenAlert(Guid assetId, AnomalyTypeEnum type, AlertSeverityEnum severity, int minutesAgo = 5) => new()
    {
        Id = Guid.NewGuid(),
        BatteryAssetId = assetId,
        AnomalyType = type,
        Severity = severity,
        Status = AlertStatusEnum.Open,
        DetectedAt = DateTime.UtcNow.AddMinutes(-minutesAgo)
    };

    [Fact]
    public async Task Topology_ParallelBank_NoSiblings_NoThermal_Returns_0_6()
    {
        // ParallelBank là kiểu nguy hiểm nhất khi lan truyền thermal runaway (current transfer
        // giữa các nhánh song song) — xem citation trong CascadeRiskCalculator.ComputeAsync.
        var id = Guid.NewGuid();
        var target = Asset(id, ElectricalTopologyEnum.ParallelBank);   // SiteId null → skip proximity

        var calc = Build(target, new() { target }, new());

        var score = await calc.CalculateAsync(id);

        score.Should().Be(0.6m);
    }

    [Fact]
    public async Task Topology_SeriesString_NoSiblings_NoThermal_Returns_0_2()
    {
        // Series lan truyền chậm nhất (chỉ qua dẫn nhiệt, không có current transfer).
        var id = Guid.NewGuid();
        var target = Asset(id, ElectricalTopologyEnum.SeriesString);

        var calc = Build(target, new() { target }, new());

        var score = await calc.CalculateAsync(id);

        score.Should().Be(0.2m);
    }

    [Fact]
    public async Task Proximity_OneSiblingOpenAlert_Adds_0_2()
    {
        var siteId = Guid.NewGuid();
        var id = Guid.NewGuid();
        var siblingId = Guid.NewGuid();
        var target = Asset(id, ElectricalTopologyEnum.Independent, siteId);   // topology 0
        var sibling = Asset(siblingId, ElectricalTopologyEnum.Independent, siteId);

        var calc = Build(target, new() { target, sibling },
            new() { OpenAlert(siblingId, AnomalyTypeEnum.Overvoltage, AlertSeverityEnum.Warning) });

        var score = await calc.CalculateAsync(id);

        score.Should().Be(0.2m);
    }

    [Fact]
    public async Task Proximity_ThreeSiblingsOpenAlerts_Adds_0_4()
    {
        var siteId = Guid.NewGuid();
        var id = Guid.NewGuid();
        var target = Asset(id, ElectricalTopologyEnum.Independent, siteId);

        var siblings = Enumerable.Range(0, 3).Select(_ => Asset(Guid.NewGuid(), ElectricalTopologyEnum.Independent, siteId)).ToList();
        var all = new List<BatteryAsset> { target };
        all.AddRange(siblings);
        var alerts = siblings.Select(s => OpenAlert(s.Id, AnomalyTypeEnum.LowSoc, AlertSeverityEnum.Warning)).ToList();

        var calc = Build(target, all, alerts);

        var score = await calc.CalculateAsync(id);

        score.Should().Be(0.4m);   // 0.2 (>=1) + 0.2 (>=3)
    }

    [Fact]
    public async Task ThermalRunaway_OverheatCriticalOpen_Adds_0_3()
    {
        var id = Guid.NewGuid();
        var target = Asset(id, ElectricalTopologyEnum.Independent);   // no site → only thermal

        var calc = Build(target, new() { target },
            new() { OpenAlert(id, AnomalyTypeEnum.Overheat, AlertSeverityEnum.Critical) });

        var score = await calc.CalculateAsync(id);

        score.Should().Be(0.3m);
    }

    [Fact]
    public async Task Combined_ExceedsOne_ClampedTo_1_0()
    {
        var siteId = Guid.NewGuid();
        var id = Guid.NewGuid();
        var target = Asset(id, ElectricalTopologyEnum.ParallelBank, siteId);   // 0.6

        var siblings = Enumerable.Range(0, 3).Select(_ => Asset(Guid.NewGuid(), ElectricalTopologyEnum.Independent, siteId)).ToList();
        var all = new List<BatteryAsset> { target };
        all.AddRange(siblings);

        var alerts = siblings.Select(s => OpenAlert(s.Id, AnomalyTypeEnum.LowSoc, AlertSeverityEnum.Warning)).ToList();
        alerts.Add(OpenAlert(id, AnomalyTypeEnum.Overheat, AlertSeverityEnum.Critical));   // +0.3 thermal

        var calc = Build(target, all, alerts);

        var score = await calc.CalculateAsync(id);

        score.Should().Be(1.0m);   // 0.6 + 0.4 + 0.3 = 1.3 → clamp
    }

    [Fact]
    public async Task UnknownAsset_Returns_0()
    {
        var calc = Build(Asset(Guid.NewGuid(), ElectricalTopologyEnum.SeriesString), new(), new());

        var score = await calc.CalculateAsync(Guid.NewGuid());

        score.Should().Be(0m);
    }

    // --- ExplainAsync (breakdown cho tooltip UI) — cùng nguồn logic với CalculateAsync ---

    [Fact]
    public async Task ExplainAsync_Independent_NoAlerts_ReturnsEmpty()
    {
        var id = Guid.NewGuid();
        var target = Asset(id, ElectricalTopologyEnum.Independent);

        var calc = Build(target, new() { target }, new());

        var reasons = await calc.ExplainAsync(id);

        reasons.Should().BeEmpty("Low risk, không rule nào đóng góp điểm thì không cần giải thích");
    }

    [Fact]
    public async Task ExplainAsync_ParallelBank_ReturnsTopologyReason()
    {
        var id = Guid.NewGuid();
        var target = Asset(id, ElectricalTopologyEnum.ParallelBank);

        var calc = Build(target, new() { target }, new());

        var reasons = await calc.ExplainAsync(id);

        reasons.Should().ContainSingle(r => r.Contains("ParallelBank") && r.Contains("0.60"));
    }

    [Fact]
    public async Task ExplainAsync_Combined_ReturnsOneReasonPerContributingRule()
    {
        var siteId = Guid.NewGuid();
        var id = Guid.NewGuid();
        var target = Asset(id, ElectricalTopologyEnum.SeriesString, siteId);
        var sibling = Asset(Guid.NewGuid(), ElectricalTopologyEnum.Independent, siteId);

        var calc = Build(target, new() { target, sibling }, new()
        {
            OpenAlert(sibling.Id, AnomalyTypeEnum.Overvoltage, AlertSeverityEnum.Warning),
            OpenAlert(id, AnomalyTypeEnum.Overheat, AlertSeverityEnum.Critical)
        });

        var reasons = await calc.ExplainAsync(id);

        // Topology (SeriesString) + Proximity (>=1 sibling) + Thermal — KHÔNG có ">=3 sibling".
        reasons.Should().HaveCount(3);
        reasons.Should().Contain(r => r.Contains("SeriesString"));
        reasons.Should().Contain(r => r.Contains("neighbouring battery"));
        reasons.Should().Contain(r => r.Contains("overheat"));
    }

    [Fact]
    public async Task ExplainAsync_UnknownAsset_ReturnsEmpty()
    {
        var calc = Build(Asset(Guid.NewGuid(), ElectricalTopologyEnum.SeriesString), new(), new());

        var reasons = await calc.ExplainAsync(Guid.NewGuid());

        reasons.Should().BeEmpty();
    }
}
