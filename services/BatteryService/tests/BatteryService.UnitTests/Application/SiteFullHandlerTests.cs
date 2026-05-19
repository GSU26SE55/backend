using BatteryService.Application.CQRS.Command.Site;
using BatteryService.Application.CQRS.Handler.Site;
using BatteryService.Application.CQRS.Query.Site;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.UnitTests.Helpers;
using SharedInfrastructure.Services;

namespace BatteryService.UnitTests.Application;

public class SiteFullHandlerTests
{
    private static readonly Guid Cust = Guid.NewGuid();

    private static CustomerAccount Customer() => new() { Id = Cust, Email = "c@x", FullName = "c", Role = "Customer", IsActive = true, LastSyncedAtUtc = DateTime.UtcNow, CreatedAt = DateTime.UtcNow };
    private static Site MakeSite(string name = "S1", bool deleted = false) => new() { Id = Guid.NewGuid(), Name = name, CustomerId = Cust, InstallDate = DateTime.UtcNow, Status = SiteStatusEnum.Active, IsDeleted = deleted, CreatedAt = DateTime.UtcNow };

    [Fact]
    public async Task Create_CustomerMissing_Returns404()
    {
        var r = await new CreateSiteCommandHandler(new MockUnitOfWorkBuilder().Build()).Handle(new CreateSiteCommand { Name = "X", CustomerId = Cust, InstallDate = DateTime.UtcNow }, default);
        r.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Create_Duplicate_Returns409()
    {
        var b = new MockUnitOfWorkBuilder().WithCustomerAccounts(Customer()).WithSites(MakeSite("Hanoi"));
        var r = await new CreateSiteCommandHandler(b.Build()).Handle(new CreateSiteCommand { Name = "hanoi", CustomerId = Cust, InstallDate = DateTime.UtcNow }, default);
        r.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Create_Happy_Returns201()
    {
        var b = new MockUnitOfWorkBuilder().WithCustomerAccounts(Customer());
        var r = await new CreateSiteCommandHandler(b.Build()).Handle(new CreateSiteCommand { Name = "HN", CustomerId = Cust, InstallDate = DateTime.UtcNow, Address = "Hanoi", Latitude = 10, Longitude = 20, CapacityKw = 100, ContactPersonName = "A", ContactPersonPhone = "0901" }, default);
        r.StatusCode.Should().Be(201);
        b.Sites.Verify(s => s.AddAsync(It.IsAny<Site>()), Times.Once);
    }

    [Fact]
    public async Task Update_NotFound_Returns404()
    {
        var r = await new UpdateSiteCommandHandler(new MockUnitOfWorkBuilder().Build()).Handle(new UpdateSiteCommand { Id = Guid.NewGuid(), Name = "x", CustomerId = Cust, InstallDate = DateTime.UtcNow }, default);
        r.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Update_ChangeCustomer_Returns409()
    {
        var s = MakeSite();
        var b = new MockUnitOfWorkBuilder().WithSites(s);
        var r = await new UpdateSiteCommandHandler(b.Build()).Handle(new UpdateSiteCommand { Id = s.Id, Name = "x", CustomerId = Guid.NewGuid(), InstallDate = DateTime.UtcNow }, default);
        r.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Update_Duplicate_Returns409()
    {
        var s1 = MakeSite("A");
        var s2 = MakeSite("B");
        var b = new MockUnitOfWorkBuilder().WithSites(s1, s2);
        var r = await new UpdateSiteCommandHandler(b.Build()).Handle(new UpdateSiteCommand { Id = s1.Id, Name = "B", CustomerId = Cust, InstallDate = DateTime.UtcNow }, default);
        r.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Update_Happy_Returns200()
    {
        var s = MakeSite("A");
        var b = new MockUnitOfWorkBuilder().WithSites(s);
        var r = await new UpdateSiteCommandHandler(b.Build()).Handle(new UpdateSiteCommand { Id = s.Id, Name = "A2", CustomerId = Cust, InstallDate = DateTime.UtcNow }, default);
        r.IsSuccess.Should().BeTrue();
        s.Name.Should().Be("A2");
    }

    [Fact]
    public async Task Delete_NotFound_Returns404()
    {
        var r = await new DeleteSiteCommandHandler(new MockUnitOfWorkBuilder().Build()).Handle(new DeleteSiteCommand { Id = Guid.NewGuid() }, default);
        r.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Delete_HasAssets_Returns409()
    {
        var s = MakeSite();
        var asset = new BatteryAsset { Id = Guid.NewGuid(), SerialNumber = "X", BatteryTypeId = Guid.NewGuid(), CustomerId = Cust, SiteId = s.Id, InstallDate = DateTime.UtcNow, CreatedAt = DateTime.UtcNow };
        var b = new MockUnitOfWorkBuilder().WithSites(s).WithBatteryAssets(asset);
        var r = await new DeleteSiteCommandHandler(b.Build()).Handle(new DeleteSiteCommand { Id = s.Id }, default);
        r.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Delete_HasGroups_Returns409()
    {
        var s = MakeSite();
        var g = new BatteryGroup { Id = Guid.NewGuid(), Name = "g", SiteId = s.Id, BatteryTypeId = Guid.NewGuid(), CreatedAt = DateTime.UtcNow };
        var b = new MockUnitOfWorkBuilder().WithSites(s).WithBatteryGroups(g);
        var r = await new DeleteSiteCommandHandler(b.Build()).Handle(new DeleteSiteCommand { Id = s.Id }, default);
        r.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Delete_Happy_Returns200()
    {
        var s = MakeSite();
        var b = new MockUnitOfWorkBuilder().WithSites(s);
        var r = await new DeleteSiteCommandHandler(b.Build()).Handle(new DeleteSiteCommand { Id = s.Id }, default);
        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Restore_NotFound_Returns404()
    {
        var b = new MockUnitOfWorkBuilder().WithSites(MakeSite());
        var r = await new RestoreSiteCommandHandler(b.Build()).Handle(new RestoreSiteCommand { Id = Guid.NewGuid() }, default);
        r.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Restore_Duplicate_Returns409()
    {
        var dead = MakeSite("A", deleted: true);
        var alive = MakeSite("A");
        var b = new MockUnitOfWorkBuilder().WithSites(dead, alive);
        var r = await new RestoreSiteCommandHandler(b.Build()).Handle(new RestoreSiteCommand { Id = dead.Id }, default);
        r.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Restore_Happy()
    {
        var dead = MakeSite(deleted: true);
        var b = new MockUnitOfWorkBuilder().WithSites(dead);
        var r = await new RestoreSiteCommandHandler(b.Build()).Handle(new RestoreSiteCommand { Id = dead.Id }, default);
        r.IsSuccess.Should().BeTrue();
        dead.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task GetById_NotFound_Returns404()
    {
        var r = await new GetSiteByIdQueryHandler(new MockUnitOfWorkBuilder().Build()).Handle(new GetSiteByIdQuery { Id = Guid.NewGuid() }, default);
        r.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetById_Found_ReturnsDto()
    {
        var s = MakeSite();
        var b = new MockUnitOfWorkBuilder().WithSites(s);
        var r = await new GetSiteByIdQueryHandler(b.Build()).Handle(new GetSiteByIdQuery { Id = s.Id }, default);
        r.IsSuccess.Should().BeTrue();
        r.Data!.Name.Should().Be("S1");
    }

    [Fact]
    public async Task GetList_AppliesFilters()
    {
        var s = MakeSite();
        var b = new MockUnitOfWorkBuilder()
            .WithCustomerAccounts(Customer())
            .WithSites(s);
        var r = await new GetSitesQueryHandler(b.Build()).Handle(new GetSitesQuery { Keyword = "s1", CustomerId = Cust, Status = SiteStatusEnum.Active }, default);
        r.Data!.TotalItems.Should().Be(1);
        r.Data.Items[0].CustomerName.Should().Be("c");
    }

    [Fact]
    public async Task GetList_IncludeDeleted()
    {
        var d = MakeSite(deleted: true);
        var b = new MockUnitOfWorkBuilder().WithSites(d);
        var r = await new GetSitesQueryHandler(b.Build()).Handle(new GetSitesQuery { IncludeDeleted = true }, default);
        r.Data!.TotalItems.Should().Be(1);
    }

    [Fact]
    public async Task GetMySites_Unauthenticated_Returns401()
    {
        var c = new Mock<ICurrentUserService>();
        c.SetupGet(x => x.UserId).Returns((string?)null);
        var r = await new GetMySitesQueryHandler(new MockUnitOfWorkBuilder().Build(), c.Object).Handle(new GetMySitesQuery(), default);
        r.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task GetMySites_Happy()
    {
        var s = MakeSite();
        var b = new MockUnitOfWorkBuilder()
            .WithCustomerAccounts(Customer())
            .WithSites(s);
        var c = new Mock<ICurrentUserService>();
        c.SetupGet(x => x.UserId).Returns(Cust.ToString());
        var r = await new GetMySitesQueryHandler(b.Build(), c.Object).Handle(new GetMySitesQuery(), default);
        r.Data!.TotalItems.Should().Be(1);
        r.Data.Items[0].CustomerName.Should().Be("c");
    }

    [Fact]
    public async Task GetSiteAssets_SiteMissing_Returns404()
    {
        var r = await new GetSiteAssetsQueryHandler(new MockUnitOfWorkBuilder().Build()).Handle(new GetSiteAssetsQuery { SiteId = Guid.NewGuid() }, default);
        r.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetSiteAssets_FiltersByGroupAndStatus()
    {
        var s = MakeSite();
        var t = new BatteryType { Id = Guid.NewGuid(), Name = "T", NominalCapacityAh = 1, NominalVoltage = 1, CreatedAt = DateTime.UtcNow };
        var g = new BatteryGroup { Id = Guid.NewGuid(), Name = "g", SiteId = s.Id, BatteryTypeId = t.Id, CreatedAt = DateTime.UtcNow };
        var asset = new BatteryAsset { Id = Guid.NewGuid(), SerialNumber = "S", BatteryTypeId = t.Id, BatteryType = t, CustomerId = Cust, SiteId = s.Id, BatteryGroupId = g.Id, InstallDate = DateTime.UtcNow, Status = BatteryStatusEnum.Active, CreatedAt = DateTime.UtcNow };
        var b = new MockUnitOfWorkBuilder().WithSites(s).WithBatteryGroups(g).WithBatteryAssets(asset);
        var r = await new GetSiteAssetsQueryHandler(b.Build()).Handle(new GetSiteAssetsQuery { SiteId = s.Id, BatteryGroupId = g.Id, Status = BatteryStatusEnum.Active }, default);
        r.Data!.TotalItems.Should().Be(1);
    }

    [Fact]
    public async Task Dashboard_SiteMissing_Returns404()
    {
        var r = await new GetSiteDashboardQueryHandler(new MockUnitOfWorkBuilder().Build()).Handle(new GetSiteDashboardQuery { Id = Guid.NewGuid() }, default);
        r.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Dashboard_NoAssets_HealthFull()
    {
        var s = MakeSite();
        var b = new MockUnitOfWorkBuilder().WithSites(s);
        var r = await new GetSiteDashboardQueryHandler(b.Build()).Handle(new GetSiteDashboardQuery { Id = s.Id }, default);
        r.Data!.HealthScore.Should().Be(100);
        r.Data.TotalAssets.Should().Be(0);
    }

    [Fact]
    public async Task Dashboard_WithAssetsAndAlerts_HealthPenalty()
    {
        var s = MakeSite();
        var t = new BatteryType { Id = Guid.NewGuid(), Name = "T", NominalCapacityAh = 1, NominalVoltage = 1, CreatedAt = DateTime.UtcNow };
        var asset1 = new BatteryAsset { Id = Guid.NewGuid(), SerialNumber = "A1", BatteryTypeId = t.Id, BatteryType = t, CustomerId = Cust, SiteId = s.Id, InstallDate = DateTime.UtcNow, Status = BatteryStatusEnum.Active, CreatedAt = DateTime.UtcNow };
        var asset2 = new BatteryAsset { Id = Guid.NewGuid(), SerialNumber = "A2", BatteryTypeId = t.Id, BatteryType = t, CustomerId = Cust, SiteId = s.Id, InstallDate = DateTime.UtcNow, Status = BatteryStatusEnum.Inactive, CreatedAt = DateTime.UtcNow };
        var alert = new Alert { Id = Guid.NewGuid(), BatteryAssetId = asset1.Id, Status = AlertStatusEnum.Open, AnomalyType = AnomalyTypeEnum.Overheat, Severity = AlertSeverityEnum.Critical, Unit = "C", DetectedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow };
        var b = new MockUnitOfWorkBuilder().WithSites(s).WithBatteryAssets(asset1, asset2).WithAlerts(alert);
        var r = await new GetSiteDashboardQueryHandler(b.Build()).Handle(new GetSiteDashboardQuery { Id = s.Id }, default);
        r.Data!.TotalAssets.Should().Be(2);
        r.Data.ActiveAssets.Should().Be(1);
        r.Data.AssetsWithActiveAlerts.Should().Be(1);
        // penalty: inactive (1*5) + alert (1*10) = 15 → 85
        r.Data.HealthScore.Should().Be(85);
    }
}
