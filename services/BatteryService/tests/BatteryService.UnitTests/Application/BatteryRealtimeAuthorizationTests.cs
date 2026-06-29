using BatteryService.Application.Helpers;
using BatteryService.Application.Realtime;

namespace BatteryService.UnitTests.Application;

/// <summary>
/// Sprint BE-IoT-Realtime (#617/#623) — test RBAC helper + scope parsing (overall.md §34.10.6).
/// </summary>
public class BatteryRealtimeAuthorizationTests
{
    private static readonly Guid Owner = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Other = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Theory]
    [InlineData("Admin")]
    [InlineData("Manager")]
    [InlineData("manager")] // case-insensitive
    public void CanAccessAsset_ManagerAdmin_AlwaysTrue(string role)
        => BatteryRealtimeAuthorizationHelper.CanAccessAsset(Owner, Other, new[] { role }).Should().BeTrue();

    [Fact]
    public void CanAccessAsset_CustomerOwnsPin_True()
        => BatteryRealtimeAuthorizationHelper.CanAccessAsset(Owner, Owner, new[] { "Customer" }).Should().BeTrue();

    [Fact]
    public void CanAccessAsset_CustomerNotOwner_False()
        => BatteryRealtimeAuthorizationHelper.CanAccessAsset(Owner, Other, new[] { "Customer" }).Should().BeFalse();

    [Fact]
    public void CanAccessAsset_Staff_True()   // MVP: Staff xem chi tiết BẤT KỲ pin
        => BatteryRealtimeAuthorizationHelper.CanAccessAsset(Owner, Other, new[] { "Staff" }).Should().BeTrue();

    [Fact]
    public void CanAccessCustomer_Staff_False()   // Staff KHÔNG xem được scope customer (dashboard khách)
        => BatteryRealtimeAuthorizationHelper.CanAccessCustomer(Owner, Other, new[] { "Staff" }).Should().BeFalse();

    [Fact]
    public void CanAccessCustomer_CustomerSelf_True()
        => BatteryRealtimeAuthorizationHelper.CanAccessCustomer(Owner, Owner, new[] { "Customer" }).Should().BeTrue();

    [Fact]
    public void CanAccessCustomer_CustomerOther_False()
        => BatteryRealtimeAuthorizationHelper.CanAccessCustomer(Owner, Other, new[] { "Customer" }).Should().BeFalse();

    [Fact]
    public void CanAccessSite_OnlyManagerAdmin()
    {
        BatteryRealtimeAuthorizationHelper.CanAccessSite(new[] { "Manager" }).Should().BeTrue();
        BatteryRealtimeAuthorizationHelper.CanAccessSite(new[] { "Admin" }).Should().BeTrue();
        BatteryRealtimeAuthorizationHelper.CanAccessSite(new[] { "Customer" }).Should().BeFalse();
        BatteryRealtimeAuthorizationHelper.CanAccessSite(new[] { "Staff" }).Should().BeFalse();
    }

    [Theory]
    [InlineData("asset:11111111-1111-1111-1111-111111111111", TelemetryScopeType.Asset)]
    [InlineData("customer:11111111-1111-1111-1111-111111111111", TelemetryScopeType.Customer)]
    [InlineData("SITE:11111111-1111-1111-1111-111111111111", TelemetryScopeType.Site)] // case-insensitive
    [InlineData("type:11111111-1111-1111-1111-111111111111", TelemetryScopeType.BatteryType)]
    public void Scope_Parse_Single(string raw, TelemetryScopeType expected)
    {
        var scope = TelemetryScope.Parse(raw);
        scope.Should().NotBeNull();
        scope!.Value.Kind.Should().Be(expected);
        scope.Value.Ids.Should().ContainSingle().Which.Should().Be(Owner);
        scope.Value.IsSingleAsset.Should().Be(expected == TelemetryScopeType.Asset);
    }

    [Fact]
    public void Scope_Parse_All_NoId()
    {
        var s = TelemetryScope.Parse("all");
        s.Should().NotBeNull();
        s!.Value.Kind.Should().Be(TelemetryScopeType.All);
        s.Value.Ids.Should().BeEmpty();
    }

    [Fact]
    public void Scope_Parse_SiteNone()
    {
        var s = TelemetryScope.Parse("site:none");
        s.Should().NotBeNull();
        s!.Value.Kind.Should().Be(TelemetryScopeType.SiteNone);
        s.Value.Ids.Should().BeEmpty();
    }

    [Fact]
    public void Scope_Parse_MultiAsset_NotSingle()
    {
        var s = TelemetryScope.Parse(
            "assets:11111111-1111-1111-1111-111111111111,22222222-2222-2222-2222-222222222222");
        s.Should().NotBeNull();
        s!.Value.Kind.Should().Be(TelemetryScopeType.Asset);
        s.Value.Ids.Should().BeEquivalentTo(new[] { Owner, Other });
        s.Value.IsSingleAsset.Should().BeFalse();   // nhiều pin → summary, không phải reading
    }

    [Fact]
    public void Scope_Parse_MultiSite()
    {
        var s = TelemetryScope.Parse(
            "sites:11111111-1111-1111-1111-111111111111,22222222-2222-2222-2222-222222222222");
        s.Should().NotBeNull();
        s!.Value.Kind.Should().Be(TelemetryScopeType.Site);
        s.Value.Ids.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("asset")]
    [InlineData("asset:not-a-guid")]
    [InlineData("unknown:11111111-1111-1111-1111-111111111111")]
    [InlineData("asset:11111111-1111-1111-1111-111111111111,22222222-2222-2222-2222-222222222222")] // single prefix + 2 id
    [InlineData("assets:")]      // multi nhưng rỗng
    [InlineData("sites:bad-guid")]
    public void Scope_Parse_Invalid_ReturnsNull(string? raw)
        => TelemetryScope.Parse(raw).Should().BeNull();
}
