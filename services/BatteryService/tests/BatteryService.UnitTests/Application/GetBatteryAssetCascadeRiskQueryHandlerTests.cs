using BatteryService.Application.CQRS.Handler.CascadeRisk;
using BatteryService.Application.CQRS.Query.CascadeRisk;
using BatteryService.Application.Services;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.UnitTests.Helpers;
using FluentAssertions;

namespace BatteryService.UnitTests.Application;

/// <summary>
/// Trực quan hoá topology (RiskFactors breakdown) — GetBatteryAssetCascadeRiskQueryHandler trả
/// score đã lưu trong DB (không recompute) nhưng gọi ICascadeRiskCalculator.ExplainAsync live để
/// giải thích lý do, chỉ để hiển thị (tooltip), không ảnh hưởng threshold-crossing.
/// </summary>
public class GetBatteryAssetCascadeRiskQueryHandlerTests
{
    private static BatteryAsset Asset(Guid id, decimal score, ElectricalTopologyEnum topology) => new()
    {
        Id = id,
        SerialNumber = "BAT-" + id.ToString()[..4],
        BatteryTypeId = Guid.NewGuid(),
        CustomerId = Guid.NewGuid(),
        InstallDate = DateTime.UtcNow,
        Status = BatteryStatusEnum.Active,
        CreatedAt = DateTime.UtcNow,
        CascadeRiskScore = score,
        ElectricalTopology = topology
    };

    [Fact]
    public async Task Handle_ExistingAsset_ReturnsStoredScoreAndLiveRiskFactors()
    {
        var id = Guid.NewGuid();
        var b = new MockUnitOfWorkBuilder().WithBatteryAssets(Asset(id, score: 0.6m, ElectricalTopologyEnum.SeriesString));
        var calc = new Mock<ICascadeRiskCalculator>();
        calc.Setup(c => c.ExplainAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "SeriesString wiring adds +0.60" });

        var sut = new GetBatteryAssetCascadeRiskQueryHandler(b.Build(), calc.Object);

        var result = await sut.Handle(new GetBatteryAssetCascadeRiskQuery { Id = id }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.CascadeRiskScore.Should().Be(0.6m, "score lấy từ DB, không recompute");
        result.Data.RiskFactors.Should().ContainSingle().Which.Should().Contain("SeriesString");
        calc.Verify(c => c.CalculateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never,
            "chỉ gọi ExplainAsync để giải thích — không recompute/ghi đè score đã lưu");
    }

    [Fact]
    public async Task Handle_NotFound_Returns404()
    {
        var b = new MockUnitOfWorkBuilder();
        var calc = new Mock<ICascadeRiskCalculator>();
        var sut = new GetBatteryAssetCascadeRiskQueryHandler(b.Build(), calc.Object);

        var result = await sut.Handle(new GetBatteryAssetCascadeRiskQuery { Id = Guid.NewGuid() }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }
}
