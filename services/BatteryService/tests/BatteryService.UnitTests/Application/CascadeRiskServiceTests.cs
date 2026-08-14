using BatteryService.Application.Services;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.UnitTests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace BatteryService.UnitTests.Application;

/// <summary>
/// Sprint Bonus NS-15 (#659, R4 decay) + NS-16 (#660, R7 OrderBy) — recompute cascade risk.
/// </summary>
public class CascadeRiskServiceTests
{
    private static BatteryAsset Asset(Guid id, decimal score = 0m, DateTime? updatedAt = null, Guid? siteId = null) => new()
    {
        Id = id,
        SerialNumber = "BAT-" + id.ToString()[..4],
        BatteryTypeId = Guid.NewGuid(),
        CustomerId = Guid.NewGuid(),
        SiteId = siteId,
        InstallDate = DateTime.UtcNow,
        Status = BatteryStatusEnum.Active,
        CreatedAt = DateTime.UtcNow,
        CascadeRiskScore = score,
        CascadeRiskUpdatedAt = updatedAt
    };

    private static Alert OpenAlert(Guid assetId) => new()
    {
        Id = Guid.NewGuid(),
        BatteryAssetId = assetId,
        AnomalyType = AnomalyTypeEnum.Overheat,
        Severity = AlertSeverityEnum.Critical,
        DetectedAt = DateTime.UtcNow,
        Status = AlertStatusEnum.Open,
        DedupWindowEndUtc = DateTime.UtcNow.AddMinutes(30)
    };

    private static (CascadeRiskService sut, Mock<ICascadeRiskCalculator> calc, Mock<IIntegrationEventOutboxWriter> outbox)
        Build(MockUnitOfWorkBuilder b)
    {
        var calc = new Mock<ICascadeRiskCalculator>();
        var outbox = new Mock<IIntegrationEventOutboxWriter>();
        var sut = new CascadeRiskService(b.Build(), calc.Object, outbox.Object, NullLogger<CascadeRiskService>.Instance);
        return (sut, calc, outbox);
    }

    [Fact]
    public async Task Decay_AssetWithScoreButNoOpenAlert_RecomputedDown()
    {
        // Sprint Bonus NS-15 (#659, R4) — pin từng High (0.8) nhưng hết alert Open → recompute → tụt.
        var assetId = Guid.NewGuid();
        var b = new MockUnitOfWorkBuilder().WithBatteryAssets(Asset(assetId, score: 0.8m, updatedAt: DateTime.UtcNow.AddHours(-1)));
        var (sut, calc, outbox) = Build(b);
        calc.Setup(c => c.CalculateAsync(assetId, It.IsAny<CancellationToken>())).ReturnsAsync(0.0m); // topology-only

        var result = await sut.RecomputeAsync(200, CancellationToken.None);

        result.Scanned.Should().Be(1, "asset có score>0 phải được recompute dù không còn alert Open");
        b.BatteryAssets.Verify(r => r.UpdateAsync(It.Is<BatteryAsset>(a => a.Id == assetId && a.CascadeRiskScore == 0.0m)), Times.Once);
        outbox.Verify(o => o.WriteAsync(It.IsAny<BatteryCascadeRiskHighEvent>(), It.IsAny<CancellationToken>()), Times.Never,
            "score đi xuống không publish event High");
    }

    [Fact]
    public async Task ActiveAsset_CrossesHigh_PublishesEvent()
    {
        var assetId = Guid.NewGuid();
        var b = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(Asset(assetId, score: 0.0m))
            .WithAlerts(OpenAlert(assetId));
        var (sut, calc, outbox) = Build(b);
        calc.Setup(c => c.CalculateAsync(assetId, It.IsAny<CancellationToken>())).ReturnsAsync(0.8m);

        var result = await sut.RecomputeAsync(200, CancellationToken.None);

        result.HighRisk.Should().Be(1);
        outbox.Verify(o => o.WriteAsync(
            It.Is<BatteryCascadeRiskHighEvent>(e => e.BatteryAssetId == assetId && e.CascadeRiskScore == 0.8m),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ActiveAsset_AlreadyHigh_DoesNotRepublish()
    {
        var assetId = Guid.NewGuid();
        var b = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(Asset(assetId, score: 0.75m))
            .WithAlerts(OpenAlert(assetId));
        var (sut, calc, outbox) = Build(b);
        calc.Setup(c => c.CalculateAsync(assetId, It.IsAny<CancellationToken>())).ReturnsAsync(0.85m);

        var result = await sut.RecomputeAsync(200, CancellationToken.None);

        result.HighRisk.Should().Be(0, "guard oldScore<0.7 — đã High thì không re-publish");
        outbox.Verify(o => o.WriteAsync(It.IsAny<BatteryCascadeRiskHighEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NoCandidates_ReturnsEmpty()
    {
        var b = new MockUnitOfWorkBuilder().WithBatteryAssets(Asset(Guid.NewGuid(), score: 0m)); // score 0, no alert
        var (sut, _, outbox) = Build(b);

        var result = await sut.RecomputeAsync(200, CancellationToken.None);

        result.Scanned.Should().Be(0);
        outbox.Verify(o => o.WriteAsync(It.IsAny<BatteryCascadeRiskHighEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Ordering_ProcessesStalestFirst_WithinBatchSize()
    {
        // Sprint Bonus NS-16 (#660, R7) — batchSize=1 → chỉ asset stale nhất (updatedAt cũ/null) được tính.
        var stale = Guid.NewGuid();   // updatedAt null → coi như MinValue → đầu tiên
        var recent = Guid.NewGuid();  // updatedAt gần đây → cuối
        var b = new MockUnitOfWorkBuilder().WithBatteryAssets(
            Asset(recent, score: 0.6m, updatedAt: DateTime.UtcNow),
            Asset(stale, score: 0.6m, updatedAt: null));
        var (sut, calc, _) = Build(b);
        calc.Setup(c => c.CalculateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(0.6m);

        var result = await sut.RecomputeAsync(batchSize: 1, CancellationToken.None);

        result.Scanned.Should().Be(1, "batchSize=1 → chỉ 1 asset/tick");
        calc.Verify(c => c.CalculateAsync(stale, It.IsAny<CancellationToken>()), Times.Once, "asset stale nhất được ưu tiên");
        calc.Verify(c => c.CalculateAsync(recent, It.IsAny<CancellationToken>()), Times.Never);
    }
}
