using BatteryService.Application.CQRS.Handler.Site;
using BatteryService.Application.CQRS.Query.Site;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.UnitTests.Helpers;

namespace BatteryService.UnitTests.Application;

public class SiteDashboardStatsHandlerTests
{
    private static readonly Guid Cust = Guid.NewGuid();

    private static Site MakeSite(SiteStatusEnum status = SiteStatusEnum.Active, bool deleted = false)
        => new() { Id = Guid.NewGuid(), Name = Guid.NewGuid().ToString("N"), CustomerId = Cust, InstallDate = DateTime.UtcNow, Status = status, IsDeleted = deleted, CreatedAt = DateTime.UtcNow };

    private static BatteryAsset MakeAsset(Guid? siteId, BatteryStatusEnum status = BatteryStatusEnum.Active, bool deleted = false)
        => new() { Id = Guid.NewGuid(), SerialNumber = Guid.NewGuid().ToString("N"), BatteryTypeId = Guid.NewGuid(), CustomerId = Cust, SiteId = siteId, InstallDate = DateTime.UtcNow, Status = status, IsDeleted = deleted, CreatedAt = DateTime.UtcNow };

    private static Alert MakeAlert(BatteryAsset asset, AlertStatusEnum status = AlertStatusEnum.Open)
        => new() { Id = Guid.NewGuid(), BatteryAssetId = asset.Id, BatteryAsset = asset, Status = status, AnomalyType = AnomalyTypeEnum.Overheat, Severity = AlertSeverityEnum.Critical, Unit = "C", DetectedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow };

    [Fact]
    public async Task Stats_Empty_ReturnsZeros()
    {
        var r = await new GetSiteDashboardStatsQueryHandler(new MockUnitOfWorkBuilder().Build())
            .Handle(new GetSiteDashboardStatsQuery(), default);

        r.IsSuccess.Should().BeTrue();
        r.Data!.Total.Should().Be(0);
        r.Data.ActiveCount.Should().Be(0);
        r.Data.TotalBatteries.Should().Be(0);
        r.Data.ActiveBatteries.Should().Be(0);
        r.Data.AvgHealth.Should().Be(0);
        r.Data.AtRiskCount.Should().Be(0);
    }

    [Fact]
    public async Task Stats_ComputesTotalsAvgHealthAndAtRisk()
    {
        // site1: 1 active + 1 inactive asset, alert trên asset active → health = 100 − 5 − 10 = 85
        var site1 = MakeSite();
        var s1A1 = MakeAsset(site1.Id);
        var s1A2 = MakeAsset(site1.Id, BatteryStatusEnum.Inactive);
        var alert1 = MakeAlert(s1A1);

        // site2: không có asset → health 100
        var site2 = MakeSite();

        // site3 (UnderMaintenance): 3 asset inactive, 1 có alert → health = 100 − 15 − 10 = 75 → at-risk
        var site3 = MakeSite(SiteStatusEnum.UnderMaintenance);
        var s3A1 = MakeAsset(site3.Id, BatteryStatusEnum.Inactive);
        var s3A2 = MakeAsset(site3.Id, BatteryStatusEnum.Inactive);
        var s3A3 = MakeAsset(site3.Id, BatteryStatusEnum.Inactive);
        var alert3 = MakeAlert(s3A1, AlertStatusEnum.Acknowledged);

        // asset chưa gán site: tính vào TotalBatteries nhưng không ảnh hưởng health site nào
        var siteless = MakeAsset(siteId: null);

        // site đã xóa: loại hoàn toàn
        var deletedSite = MakeSite(deleted: true);

        var b = new MockUnitOfWorkBuilder()
            .WithSites(site1, site2, site3, deletedSite)
            .WithBatteryAssets(s1A1, s1A2, s3A1, s3A2, s3A3, siteless)
            .WithAlerts(alert1, alert3);

        var r = await new GetSiteDashboardStatsQueryHandler(b.Build())
            .Handle(new GetSiteDashboardStatsQuery(), default);

        r.Data!.Total.Should().Be(3);
        r.Data.ActiveCount.Should().Be(2); // site1 + site2
        r.Data.TotalBatteries.Should().Be(6);
        r.Data.ActiveBatteries.Should().Be(2); // s1A1 + siteless
        r.Data.AvgHealth.Should().BeApproximately((85 + 100 + 75) / 3d, 0.01);
        r.Data.AtRiskCount.Should().Be(1); // site3 (75 < 80)
    }

    [Fact]
    public async Task Stats_IgnoresResolvedAlerts_AndDeletedAssets()
    {
        var site = MakeSite();
        var asset = MakeAsset(site.Id);
        var resolvedAlert = MakeAlert(asset, AlertStatusEnum.Resolved); // không còn active → không phạt

        var deletedAsset = MakeAsset(site.Id, deleted: true);
        var alertOnDeleted = MakeAlert(deletedAsset); // asset đã xóa → không phạt

        var b = new MockUnitOfWorkBuilder()
            .WithSites(site)
            .WithBatteryAssets(asset, deletedAsset)
            .WithAlerts(resolvedAlert, alertOnDeleted);

        var r = await new GetSiteDashboardStatsQueryHandler(b.Build())
            .Handle(new GetSiteDashboardStatsQuery(), default);

        r.Data!.TotalBatteries.Should().Be(1);
        r.Data.AvgHealth.Should().Be(100);
        r.Data.AtRiskCount.Should().Be(0);
    }
}
