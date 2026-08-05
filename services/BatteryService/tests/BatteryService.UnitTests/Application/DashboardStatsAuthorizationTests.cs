using BatteryService.Application.Anomaly;
using BatteryService.Application.CQRS.Handler.Dashboard;
using BatteryService.Application.CQRS.Query.Dashboard;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.UnitTests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace BatteryService.UnitTests.Application;

/// <summary>
/// GH-774 — thống kê dashboard lộ cho mọi vai trò đã đăng nhập.
///
/// <para>
/// Controller chỉ có <c>[Authorize]</c>, trong khi chính tài liệu của endpoint ghi "trả tổng hợp
/// toàn bộ system (yêu cầu role Admin/Manager)". Đo được lúc chạy thật qua ApiGateway: Manager,
/// Staff và Customer đều nhận 200 với <c>totalAssets=10</c> — toàn bộ số liệu đội tàu, cảnh báo và
/// phân phối SOH của mọi khách hàng bị lộ.
/// </para>
/// <para>
/// Chính sách áp dụng bám <c>BatteryTenantScopeHelper</c> (spec §34.10.6): Staff VẪN xem được mọi
/// asset — đó là quyết định MVP có chủ ý, không phải lỗi. Cái bị chặn là con số GỘP toàn hệ thống,
/// vốn là thông tin điều hành chứ không phải thứ cần để xử lý ticket.
/// </para>
/// </summary>
public class DashboardStatsAuthorizationTests
{
    private static readonly Guid CustomerA = Guid.NewGuid();
    private static readonly Guid CustomerB = Guid.NewGuid();

    private static (MockUnitOfWorkBuilder Uow, Guid SiteOfA, Guid SiteOfB) SeedTwoTenants()
    {
        var siteA = new Site { Id = Guid.NewGuid(), Name = "Site A", CustomerId = CustomerA };
        var siteB = new Site { Id = Guid.NewGuid(), Name = "Site B", CustomerId = CustomerB };

        var uow = new MockUnitOfWorkBuilder()
            .WithSites(siteA, siteB)
            .WithBatteryAssets(
                new BatteryAsset
                {
                    Id = Guid.NewGuid(), SerialNumber = "BAT-A1", SiteId = siteA.Id,
                    CustomerId = CustomerA, Status = BatteryStatusEnum.Active,
                },
                new BatteryAsset
                {
                    Id = Guid.NewGuid(), SerialNumber = "BAT-B1", SiteId = siteB.Id,
                    CustomerId = CustomerB, Status = BatteryStatusEnum.Active,
                },
                new BatteryAsset
                {
                    Id = Guid.NewGuid(), SerialNumber = "BAT-B2", SiteId = siteB.Id,
                    CustomerId = CustomerB, Status = BatteryStatusEnum.Active,
                });

        return (uow, siteA.Id, siteB.Id);
    }

    private static GetBatteryDashboardStatsQueryHandler Handler(
        MockUnitOfWorkBuilder uow, IBatteryCurrentUserService user)
        => new(uow.Build(), Options.Create(new AnomalyEngineOptions()), user);

    [Fact]
    public async Task GlobalStats_Customer_IsForbidden()
    {
        var (uow, _, _) = SeedTwoTenants();

        var resp = await Handler(uow, TestBatteryCurrentUserService.Customer(CustomerA))
            .Handle(new GetBatteryDashboardStatsQuery { SiteId = null }, CancellationToken.None);

        resp.IsSuccess.Should().BeFalse();
        resp.StatusCode.Should().Be(403);
        resp.Data.Should().BeNull("không được rò một phần số liệu kèm theo lỗi");
    }

    [Fact]
    public async Task GlobalStats_Staff_IsForbidden()
    {
        // Staff xem được mọi ASSET (§34.10.6) nhưng con số gộp toàn hệ thống là thông tin điều
        // hành — tài liệu của endpoint đã nói Admin/Manager từ đầu.
        var (uow, _, _) = SeedTwoTenants();

        var resp = await Handler(uow, TestBatteryCurrentUserService.Staff())
            .Handle(new GetBatteryDashboardStatsQuery { SiteId = null }, CancellationToken.None);

        resp.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task GlobalStats_CustomerWithBrokenToken_IsForbidden()
    {
        // Fail closed: token không đọc được id thì không có đường nào coi là hợp lệ.
        var (uow, _, _) = SeedTwoTenants();

        var resp = await Handler(uow, TestBatteryCurrentUserService.CustomerWithBrokenToken())
            .Handle(new GetBatteryDashboardStatsQuery { SiteId = null }, CancellationToken.None);

        resp.StatusCode.Should().Be(403);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Manager")]
    public async Task GlobalStats_AdminAndManager_StillWork(string role)
    {
        // Chống hồi quy: bản sửa không được chặn nhầm chính hai vai trò mà dashboard sinh ra để phục vụ.
        var (uow, _, _) = SeedTwoTenants();
        var user = role == "Admin"
            ? TestBatteryCurrentUserService.Admin()
            : TestBatteryCurrentUserService.Manager();

        var resp = await Handler(uow, user)
            .Handle(new GetBatteryDashboardStatsQuery { SiteId = null }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.Data!.TotalAssets.Should().Be(3);
    }

    [Fact]
    public async Task SiteStats_CustomerOwningTheSite_IsAllowed_AndScopedToThatSite()
    {
        var (uow, siteOfA, _) = SeedTwoTenants();

        var resp = await Handler(uow, TestBatteryCurrentUserService.Customer(CustomerA))
            .Handle(new GetBatteryDashboardStatsQuery { SiteId = siteOfA }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.Data!.TotalAssets.Should().Be(1, "chỉ site của chính mình");
    }

    [Fact]
    public async Task SiteStats_CustomerAskingForAnotherTenantsSite_IsRejected()
    {
        // ĐÂY là lỗ mà thêm attribute ở controller KHÔNG bịt được: token hợp lệ, vai trò hợp lệ,
        // chỉ có siteId là của người khác.
        var (uow, _, siteOfB) = SeedTwoTenants();

        var resp = await Handler(uow, TestBatteryCurrentUserService.Customer(CustomerA))
            .Handle(new GetBatteryDashboardStatsQuery { SiteId = siteOfB }, CancellationToken.None);

        resp.IsSuccess.Should().BeFalse();
        // 404 chứ không 403: 403 xác nhận site đó CÓ THẬT, biến endpoint thành công cụ dò xem
        // khách hàng nào đang tồn tại. Khớp quy ước GH-722.
        resp.StatusCode.Should().Be(404);
        resp.Data.Should().BeNull();
    }

    [Fact]
    public async Task SiteStats_Staff_CanReadAnySite()
    {
        // §34.10.6: Staff xử lý ticket/bảo trì trên pin bất kỳ — chặn ở đây là làm hỏng nghiệp vụ.
        var (uow, _, siteOfB) = SeedTwoTenants();

        var resp = await Handler(uow, TestBatteryCurrentUserService.Staff())
            .Handle(new GetBatteryDashboardStatsQuery { SiteId = siteOfB }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.Data!.TotalAssets.Should().Be(2);
    }

    [Fact]
    public async Task SiteStats_NonExistentSite_IsRejectedForCustomer()
    {
        var (uow, _, _) = SeedTwoTenants();

        var resp = await Handler(uow, TestBatteryCurrentUserService.Customer(CustomerA))
            .Handle(new GetBatteryDashboardStatsQuery { SiteId = Guid.NewGuid() }, CancellationToken.None);

        resp.StatusCode.Should().Be(404);
    }
}
