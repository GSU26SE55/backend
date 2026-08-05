using BatteryService.Application.CQRS.Command.Alert;
using BatteryService.Application.CQRS.Handler.Alert;
using BatteryService.Application.CQRS.Handler.BatteryAsset;
using BatteryService.Application.CQRS.Handler.Site;
using BatteryService.Application.CQRS.Query.Alert;
using BatteryService.Application.CQRS.Query.BatteryAsset;
using BatteryService.Application.CQRS.Query.Site;
using BatteryService.Application.Helpers;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.UnitTests.Helpers;
using FluentAssertions;

namespace BatteryService.UnitTests.Application;

/// <summary>
/// GH-722 — Customer không được đọc/ACK tài nguyên ngoài tenant của mình.
///
/// Mọi test ở đây ĐỎ trên code trước khi sửa (handler chỉ lọc theo Id) và XANH sau khi sửa.
/// Không chỉ assert HTTP 200/404: ca ACK còn assert trạng thái alert KHÔNG bị đổi.
/// </summary>
public class TenantIsolationGh722Tests
{
    private static readonly Guid CustomerA = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid CustomerB = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

    private static BatteryType MakeType() => new()
    {
        Id = Guid.NewGuid(),
        Name = "T",
        NominalCapacityAh = 100,
        NominalVoltage = 48,
        CreatedAt = DateTime.UtcNow
    };

    private static BatteryAsset MakeAsset(Guid ownerId, BatteryType type, Guid? siteId = null) => new()
    {
        Id = Guid.NewGuid(),
        SerialNumber = "SN-" + Guid.NewGuid().ToString("N")[..6],
        BatteryTypeId = type.Id,
        BatteryType = type,
        CustomerId = ownerId,
        SiteId = siteId,
        InstallDate = DateTime.UtcNow,
        Status = BatteryStatusEnum.Active,
        CreatedAt = DateTime.UtcNow
    };

    private static Site MakeSite(Guid ownerId) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Site-" + Guid.NewGuid().ToString("N")[..6],
        CustomerId = ownerId,
        InstallDate = DateTime.UtcNow,
        CreatedAt = DateTime.UtcNow
    };

    private static Alert MakeAlert(BatteryAsset asset) => new()
    {
        Id = Guid.NewGuid(),
        BatteryAssetId = asset.Id,
        BatteryAsset = asset,
        Status = AlertStatusEnum.Open,
        AnomalyType = AnomalyTypeEnum.Overheat,
        Severity = AlertSeverityEnum.Critical,
        Unit = "C",
        DetectedAt = DateTime.UtcNow,
        CreatedAt = DateTime.UtcNow
    };

    // ───────────────────────── BatteryAsset ─────────────────────────

    [Fact]
    public async Task GetAssetById_CrossTenant_Returns404()
    {
        var type = MakeType();
        var assetOfB = MakeAsset(CustomerB, type);
        var uow = new MockUnitOfWorkBuilder().WithBatteryAssets(assetOfB).Build();

        var result = await new GetBatteryAssetByIdQueryHandler(uow, TestBatteryCurrentUserService.Customer(CustomerA))
            .Handle(new GetBatteryAssetByIdQuery { Id = assetOfB.Id }, default);

        result.StatusCode.Should().Be(404);
        result.IsSuccess.Should().BeFalse();
        result.Data.Should().BeNull();
    }

    [Fact]
    public async Task GetAssetById_OwnAsset_Returns200()
    {
        var type = MakeType();
        var assetOfA = MakeAsset(CustomerA, type);
        var uow = new MockUnitOfWorkBuilder().WithBatteryAssets(assetOfA).Build();

        var result = await new GetBatteryAssetByIdQueryHandler(uow, TestBatteryCurrentUserService.Customer(CustomerA))
            .Handle(new GetBatteryAssetByIdQuery { Id = assetOfA.Id }, default);

        result.StatusCode.Should().Be(200);
        result.Data!.Id.Should().Be(assetOfA.Id.ToString());
    }

    [Fact]
    public async Task GetAssetById_Staff_SeesAnyAsset()
    {
        // Chính sách §34.10.6: Staff xử lý ticket/bảo trì trên pin bất kỳ.
        var type = MakeType();
        var assetOfB = MakeAsset(CustomerB, type);
        var uow = new MockUnitOfWorkBuilder().WithBatteryAssets(assetOfB).Build();

        var result = await new GetBatteryAssetByIdQueryHandler(uow, TestBatteryCurrentUserService.Staff())
            .Handle(new GetBatteryAssetByIdQuery { Id = assetOfB.Id }, default);

        result.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task GetAssetById_CustomerWithBrokenToken_FailsClosed401()
    {
        var type = MakeType();
        var assetOfB = MakeAsset(CustomerB, type);
        var uow = new MockUnitOfWorkBuilder().WithBatteryAssets(assetOfB).Build();

        var result = await new GetBatteryAssetByIdQueryHandler(uow, TestBatteryCurrentUserService.CustomerWithBrokenToken())
            .Handle(new GetBatteryAssetByIdQuery { Id = assetOfB.Id }, default);

        // Token hỏng KHÔNG được biến thành quyền xem tất cả.
        result.StatusCode.Should().Be(401);
        result.Data.Should().BeNull();
    }

    [Fact]
    public async Task GetAssetRealtime_CrossTenant_Returns404()
    {
        var type = MakeType();
        var assetOfB = MakeAsset(CustomerB, type);
        var uow = new MockUnitOfWorkBuilder().WithBatteryAssets(assetOfB).Build();

        var result = await new GetBatteryAssetRealtimeQueryHandler(uow, TestBatteryCurrentUserService.Customer(CustomerA))
            .Handle(new GetBatteryAssetRealtimeQuery { Id = assetOfB.Id }, default);

        result.StatusCode.Should().Be(404);
    }

    // ───────────────────────── Alert ─────────────────────────

    [Fact]
    public async Task GetAlertById_CrossTenant_Returns404()
    {
        var type = MakeType();
        var alertOfB = MakeAlert(MakeAsset(CustomerB, type));
        var uow = new MockUnitOfWorkBuilder().WithAlerts(alertOfB).Build();

        var result = await new GetAlertByIdQueryHandler(uow, TestBatteryCurrentUserService.Customer(CustomerA))
            .Handle(new GetAlertByIdQuery { Id = alertOfB.Id }, default);

        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetAlerts_List_OnlyOwnTenant()
    {
        var type = MakeType();
        var alertOfA = MakeAlert(MakeAsset(CustomerA, type));
        var alertOfB = MakeAlert(MakeAsset(CustomerB, type));
        var uow = new MockUnitOfWorkBuilder().WithAlerts(alertOfA, alertOfB).Build();

        var result = await new GetAlertsQueryHandler(uow, TestBatteryCurrentUserService.Customer(CustomerA))
            .Handle(new GetAlertsQuery { PageNumber = 1, PageSize = 50 }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Items.Should().ContainSingle();
        result.Data.Items[0].Id.Should().Be(alertOfA.Id.ToString());
    }

    [Fact]
    public async Task AcknowledgeAlert_CrossTenant_Returns404_AndDoesNotMutate()
    {
        var type = MakeType();
        var alertOfB = MakeAlert(MakeAsset(CustomerB, type));
        var uow = new MockUnitOfWorkBuilder().WithAlerts(alertOfB).Build();

        var result = await new AcknowledgeAlertCommandHandler(uow, TestBatteryCurrentUserService.Customer(CustomerA))
            .Handle(new AcknowledgeAlertCommand { Id = alertOfB.Id }, default);

        result.StatusCode.Should().Be(404);

        // Quan trọng hơn mã lỗi: trạng thái alert của tenant khác phải NGUYÊN VẸN.
        alertOfB.Status.Should().Be(AlertStatusEnum.Open);
        alertOfB.AcknowledgedAt.Should().BeNull();
        alertOfB.AcknowledgedByUserId.Should().BeNull();
    }

    [Fact]
    public async Task AcknowledgeAlert_OwnAlert_Succeeds()
    {
        var type = MakeType();
        var alertOfA = MakeAlert(MakeAsset(CustomerA, type));
        var uow = new MockUnitOfWorkBuilder().WithAlerts(alertOfA).Build();

        var result = await new AcknowledgeAlertCommandHandler(uow, TestBatteryCurrentUserService.Customer(CustomerA))
            .Handle(new AcknowledgeAlertCommand { Id = alertOfA.Id }, default);

        result.IsSuccess.Should().BeTrue();
        alertOfA.Status.Should().Be(AlertStatusEnum.Acknowledged);
        alertOfA.AcknowledgedByUserId.Should().Be(CustomerA);
    }

    // ───────────────────────── Site ─────────────────────────

    [Fact]
    public async Task GetSiteById_CrossTenant_Returns404()
    {
        var siteOfB = MakeSite(CustomerB);
        var uow = new MockUnitOfWorkBuilder().WithSites(siteOfB).Build();

        var result = await new GetSiteByIdQueryHandler(uow, TestBatteryCurrentUserService.Customer(CustomerA))
            .Handle(new GetSiteByIdQuery { Id = siteOfB.Id }, default);

        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetSiteAssets_CrossTenant_Returns404()
    {
        var type = MakeType();
        var siteOfB = MakeSite(CustomerB);
        var assetOfB = MakeAsset(CustomerB, type, siteOfB.Id);
        var uow = new MockUnitOfWorkBuilder().WithSites(siteOfB).WithBatteryAssets(assetOfB).Build();

        var result = await new GetSiteAssetsQueryHandler(uow, TestBatteryCurrentUserService.Customer(CustomerA))
            .Handle(new GetSiteAssetsQuery { SiteId = siteOfB.Id, PageNumber = 1, PageSize = 20 }, default);

        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetSiteDashboard_CrossTenant_Returns404()
    {
        var siteOfB = MakeSite(CustomerB);
        var uow = new MockUnitOfWorkBuilder().WithSites(siteOfB).Build();

        var result = await new GetSiteDashboardQueryHandler(uow, TestBatteryCurrentUserService.Customer(CustomerA))
            .Handle(new GetSiteDashboardQuery { Id = siteOfB.Id }, default);

        result.StatusCode.Should().Be(404);
    }

    // ───────────────────── Helper thuần ─────────────────────

    [Fact]
    public void Resolve_AdminAndManager_Unrestricted()
    {
        BatteryTenantScopeHelper.Resolve(Guid.NewGuid().ToString(), new[] { "Admin" })
            .IsUnrestricted.Should().BeTrue();
        BatteryTenantScopeHelper.Resolve(Guid.NewGuid().ToString(), new[] { "Manager" })
            .IsUnrestricted.Should().BeTrue();
    }

    [Fact]
    public void Resolve_Customer_ScopedToSelf()
    {
        var scope = BatteryTenantScopeHelper.Resolve(CustomerA.ToString(), new[] { "Customer" });
        scope.IsCustomerScoped.Should().BeTrue();
        scope.CustomerId.Should().Be(CustomerA);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("không-phải-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void Resolve_CustomerWithUnusableId_Denied(string? userId)
    {
        BatteryTenantScopeHelper.Resolve(userId, new[] { "Customer" })
            .IsDenied.Should().BeTrue();
    }

    [Fact]
    public void Resolve_NoRole_Denied()
    {
        BatteryTenantScopeHelper.Resolve(Guid.NewGuid().ToString(), Array.Empty<string>())
            .IsDenied.Should().BeTrue();
    }

    [Fact]
    public void Resolve_RoleIsCaseInsensitive()
    {
        BatteryTenantScopeHelper.Resolve(CustomerA.ToString(), new[] { "customer" })
            .IsCustomerScoped.Should().BeTrue();
        BatteryTenantScopeHelper.Resolve(Guid.NewGuid().ToString(), new[] { "aDmIn" })
            .IsUnrestricted.Should().BeTrue();
    }
}
