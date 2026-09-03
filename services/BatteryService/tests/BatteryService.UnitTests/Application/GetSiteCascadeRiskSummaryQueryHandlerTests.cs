using BatteryService.Application.CQRS.Handler.CascadeRisk;
using BatteryService.Application.CQRS.Query.CascadeRisk;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.UnitTests.Helpers;
using FluentAssertions;

namespace BatteryService.UnitTests.Application;

/// <summary>
/// Trực quan hoá topology theo site — TopologyBreakdown đếm trên TOÀN BỘ asset của site (không
/// phân trang), để khớp với bảng battery list vốn phân trang và không đủ để suy ra tổng site.
/// </summary>
public class GetSiteCascadeRiskSummaryQueryHandlerTests
{
    private static Site TestSite(Guid id) => new()
    {
        Id = id,
        Name = "Test Site",
        CustomerId = Guid.NewGuid(),
        InstallDate = DateTime.UtcNow
    };

    private static BatteryAsset Asset(Guid siteId, ElectricalTopologyEnum topology, decimal score = 0m) => new()
    {
        Id = Guid.NewGuid(),
        SerialNumber = "BAT-" + Guid.NewGuid().ToString()[..4],
        BatteryTypeId = Guid.NewGuid(),
        CustomerId = Guid.NewGuid(),
        SiteId = siteId,
        InstallDate = DateTime.UtcNow,
        Status = BatteryStatusEnum.Active,
        CreatedAt = DateTime.UtcNow,
        ElectricalTopology = topology,
        CascadeRiskScore = score
    };

    [Fact]
    public async Task Handle_CountsTopologyAcrossAllAssets_NotJustHighRisk()
    {
        var siteId = Guid.NewGuid();
        var b = new MockUnitOfWorkBuilder()
            .WithSites(TestSite(siteId))
            .WithBatteryAssets(
                Asset(siteId, ElectricalTopologyEnum.Independent),
                Asset(siteId, ElectricalTopologyEnum.Independent),
                Asset(siteId, ElectricalTopologyEnum.SeriesString, score: 0.8m), // duy nhất High risk
                Asset(siteId, ElectricalTopologyEnum.ParallelBank),
                Asset(siteId, ElectricalTopologyEnum.SeriesParallel));

        var sut = new GetSiteCascadeRiskSummaryQueryHandler(b.Build());

        var result = await sut.Handle(new GetSiteCascadeRiskSummaryQuery { SiteId = siteId }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // Đếm trên CẢ 5 asset — không chỉ riêng HighRiskAssets (chỉ có 1 phần tử).
        result.Data!.IndependentCount.Should().Be(2);
        result.Data.SeriesStringCount.Should().Be(1);
        result.Data.ParallelBankCount.Should().Be(1);
        result.Data.SeriesParallelCount.Should().Be(1);
        result.Data.HighRiskAssets.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_SiteNotFound_Returns404()
    {
        var b = new MockUnitOfWorkBuilder();
        var sut = new GetSiteCascadeRiskSummaryQueryHandler(b.Build());

        var result = await sut.Handle(new GetSiteCascadeRiskSummaryQuery { SiteId = Guid.NewGuid() }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }
}
