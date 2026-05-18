using BatteryService.Application.CQRS.Command.BatteryGroup;
using BatteryService.Application.CQRS.Handler.BatteryGroup;
using BatteryService.Application.CQRS.Query.BatteryGroup;
using BatteryService.Domain.Entities;
using BatteryService.UnitTests.Helpers;

namespace BatteryService.UnitTests.Application;

public class BatteryGroupFullHandlerTests
{
    private static readonly Guid Cust = Guid.NewGuid();

    private static Site MakeSite(bool deleted = false) => new() { Id = Guid.NewGuid(), Name = "S", CustomerId = Cust, InstallDate = DateTime.UtcNow, IsDeleted = deleted, CreatedAt = DateTime.UtcNow };
    private static BatteryType MakeType(bool deleted = false) => new() { Id = Guid.NewGuid(), Name = "T", NominalCapacityAh = 1, NominalVoltage = 1, IsDeleted = deleted, CreatedAt = DateTime.UtcNow };
    private static BatteryGroup MakeGroup(Site s, BatteryType t, string name = "G", bool deleted = false) => new() { Id = Guid.NewGuid(), Site = s, SiteId = s.Id, BatteryType = t, BatteryTypeId = t.Id, Name = name, IsDeleted = deleted, CreatedAt = DateTime.UtcNow };

    [Fact]
    public async Task Create_SiteMissing_404()
    {
        var t = MakeType();
        var b = new MockUnitOfWorkBuilder().WithBatteryTypes(t);
        var r = await new CreateBatteryGroupCommandHandler(b.Build()).Handle(new CreateBatteryGroupCommand { SiteId = Guid.NewGuid(), Name = "g", BatteryTypeId = t.Id }, default);
        r.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Create_TypeMissing_404()
    {
        var s = MakeSite();
        var b = new MockUnitOfWorkBuilder().WithSites(s);
        var r = await new CreateBatteryGroupCommandHandler(b.Build()).Handle(new CreateBatteryGroupCommand { SiteId = s.Id, Name = "g", BatteryTypeId = Guid.NewGuid() }, default);
        r.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Create_Duplicate_409()
    {
        var s = MakeSite();
        var t = MakeType();
        var g = MakeGroup(s, t, "Same");
        var b = new MockUnitOfWorkBuilder().WithSites(s).WithBatteryTypes(t).WithBatteryGroups(g);
        var r = await new CreateBatteryGroupCommandHandler(b.Build()).Handle(new CreateBatteryGroupCommand { SiteId = s.Id, Name = "same", BatteryTypeId = t.Id }, default);
        r.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Create_Happy_201()
    {
        var s = MakeSite();
        var t = MakeType();
        var b = new MockUnitOfWorkBuilder().WithSites(s).WithBatteryTypes(t);
        var r = await new CreateBatteryGroupCommandHandler(b.Build()).Handle(new CreateBatteryGroupCommand { SiteId = s.Id, Name = "G1", BatteryTypeId = t.Id }, default);
        r.StatusCode.Should().Be(201);
    }

    [Fact]
    public async Task Update_NotFound_404()
    {
        var r = await new UpdateBatteryGroupCommandHandler(new MockUnitOfWorkBuilder().Build()).Handle(new UpdateBatteryGroupCommand { Id = Guid.NewGuid(), SiteId = Guid.NewGuid(), Name = "x", BatteryTypeId = Guid.NewGuid() }, default);
        r.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Update_ChangeWithAssets_409()
    {
        var s = MakeSite();
        var t = MakeType();
        var g = MakeGroup(s, t);
        var newSite = MakeSite();
        var asset = new BatteryAsset { Id = Guid.NewGuid(), SerialNumber = "A", BatteryTypeId = t.Id, CustomerId = Cust, BatteryGroupId = g.Id, InstallDate = DateTime.UtcNow, CreatedAt = DateTime.UtcNow };
        var b = new MockUnitOfWorkBuilder().WithSites(s, newSite).WithBatteryTypes(t).WithBatteryGroups(g).WithBatteryAssets(asset);
        var r = await new UpdateBatteryGroupCommandHandler(b.Build()).Handle(new UpdateBatteryGroupCommand { Id = g.Id, SiteId = newSite.Id, Name = "x", BatteryTypeId = t.Id }, default);
        r.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Update_DuplicateName_409()
    {
        var s = MakeSite();
        var t = MakeType();
        var g1 = MakeGroup(s, t, "A");
        var g2 = MakeGroup(s, t, "B");
        var b = new MockUnitOfWorkBuilder().WithSites(s).WithBatteryTypes(t).WithBatteryGroups(g1, g2);
        var r = await new UpdateBatteryGroupCommandHandler(b.Build()).Handle(new UpdateBatteryGroupCommand { Id = g1.Id, SiteId = s.Id, Name = "B", BatteryTypeId = t.Id }, default);
        r.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Update_Happy_200()
    {
        var s = MakeSite();
        var t = MakeType();
        var g = MakeGroup(s, t, "A");
        var b = new MockUnitOfWorkBuilder().WithSites(s).WithBatteryTypes(t).WithBatteryGroups(g);
        var r = await new UpdateBatteryGroupCommandHandler(b.Build()).Handle(new UpdateBatteryGroupCommand { Id = g.Id, SiteId = s.Id, Name = "A2", BatteryTypeId = t.Id }, default);
        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Delete_NotFound_404()
    {
        var r = await new DeleteBatteryGroupCommandHandler(new MockUnitOfWorkBuilder().Build()).Handle(new DeleteBatteryGroupCommand { Id = Guid.NewGuid() }, default);
        r.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Delete_HasAssets_409()
    {
        var s = MakeSite();
        var t = MakeType();
        var g = MakeGroup(s, t);
        var asset = new BatteryAsset { Id = Guid.NewGuid(), SerialNumber = "A", BatteryTypeId = t.Id, CustomerId = Cust, BatteryGroupId = g.Id, InstallDate = DateTime.UtcNow, CreatedAt = DateTime.UtcNow };
        var b = new MockUnitOfWorkBuilder().WithBatteryGroups(g).WithBatteryAssets(asset);
        var r = await new DeleteBatteryGroupCommandHandler(b.Build()).Handle(new DeleteBatteryGroupCommand { Id = g.Id }, default);
        r.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Delete_Happy()
    {
        var s = MakeSite();
        var t = MakeType();
        var g = MakeGroup(s, t);
        var b = new MockUnitOfWorkBuilder().WithBatteryGroups(g);
        var r = await new DeleteBatteryGroupCommandHandler(b.Build()).Handle(new DeleteBatteryGroupCommand { Id = g.Id }, default);
        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Restore_NotFound_404()
    {
        var r = await new RestoreBatteryGroupCommandHandler(new MockUnitOfWorkBuilder().Build()).Handle(new RestoreBatteryGroupCommand { Id = Guid.NewGuid() }, default);
        r.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Restore_SiteDeleted_409()
    {
        var s = MakeSite(deleted: true);
        var t = MakeType();
        var dead = MakeGroup(s, t, deleted: true);
        var b = new MockUnitOfWorkBuilder().WithSites(s).WithBatteryGroups(dead);
        var r = await new RestoreBatteryGroupCommandHandler(b.Build()).Handle(new RestoreBatteryGroupCommand { Id = dead.Id }, default);
        r.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Restore_DuplicateName_409()
    {
        var s = MakeSite();
        var t = MakeType();
        var dead = MakeGroup(s, t, "A", deleted: true);
        var alive = MakeGroup(s, t, "A");
        var b = new MockUnitOfWorkBuilder().WithSites(s).WithBatteryGroups(dead, alive);
        var r = await new RestoreBatteryGroupCommandHandler(b.Build()).Handle(new RestoreBatteryGroupCommand { Id = dead.Id }, default);
        r.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Restore_Happy()
    {
        var s = MakeSite();
        var t = MakeType();
        var dead = MakeGroup(s, t, deleted: true);
        var b = new MockUnitOfWorkBuilder().WithSites(s).WithBatteryGroups(dead);
        var r = await new RestoreBatteryGroupCommandHandler(b.Build()).Handle(new RestoreBatteryGroupCommand { Id = dead.Id }, default);
        r.IsSuccess.Should().BeTrue();
        dead.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task GetById_NotFound_404()
    {
        var r = await new GetBatteryGroupByIdQueryHandler(new MockUnitOfWorkBuilder().Build()).Handle(new GetBatteryGroupByIdQuery { Id = Guid.NewGuid() }, default);
        r.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetById_Found_Dto()
    {
        var s = MakeSite();
        var t = MakeType();
        var g = MakeGroup(s, t);
        var b = new MockUnitOfWorkBuilder().WithBatteryGroups(g);
        var r = await new GetBatteryGroupByIdQueryHandler(b.Build()).Handle(new GetBatteryGroupByIdQuery { Id = g.Id }, default);
        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task GetList_Filters()
    {
        var s = MakeSite();
        var t = MakeType();
        var g = MakeGroup(s, t, "Alpha");
        var b = new MockUnitOfWorkBuilder().WithBatteryGroups(g);
        var r = await new GetBatteryGroupsQueryHandler(b.Build()).Handle(new GetBatteryGroupsQuery { Keyword = "alpha", SiteId = s.Id, BatteryTypeId = t.Id }, default);
        r.Data!.TotalItems.Should().Be(1);
    }

    [Fact]
    public async Task GetList_IncludeDeleted()
    {
        var s = MakeSite();
        var t = MakeType();
        var g = MakeGroup(s, t, deleted: true);
        var b = new MockUnitOfWorkBuilder().WithBatteryGroups(g);
        var r = await new GetBatteryGroupsQueryHandler(b.Build()).Handle(new GetBatteryGroupsQuery { IncludeDeleted = true }, default);
        r.Data!.TotalItems.Should().Be(1);
    }
}
