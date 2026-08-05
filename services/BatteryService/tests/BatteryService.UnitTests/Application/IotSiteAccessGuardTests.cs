using BatteryService.Application.Helpers;
using FluentAssertions;
using Xunit;

namespace BatteryService.UnitTests.Application;

/// <summary>
/// GH-806 — thiết bị IoT chỉ được ghi dữ liệu cho ĐÚNG site của nó.
/// </summary>
/// <remarks>
/// <para>
/// Đo được lúc chạy thật: thiết bị thuộc Site A, khoá API scope 15, gửi ambient hợp lệ cho một
/// Site B đang tồn tại ⇒ HTTP 201, <c>inserted=1</c>. Gửi GUID site không tồn tại ⇒ 500 do lỗi khoá
/// ngoại thay vì một mã 4xx nói rõ.
/// </para>
/// <para>
/// Đây không phải bất tiện: ambient và environmental incident là dữ liệu AN TOÀN (khói, gas, ngập).
/// Một thiết bị bị chiếm quyền có thể đầu độc dữ liệu hoặc tạo sự cố giả cho khách hàng khác.
/// </para>
/// </remarks>
public class IotSiteAccessGuardTests
{
    private static readonly Guid SiteA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid SiteB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid Ghost = Guid.Parse("cccccccc-0000-0000-0000-000000000003");

    [Fact]
    public void DeviceWritingToItsOwnSite_IsAllowed()
    {
        IotSiteAccessGuard.Check(SiteA, [SiteA], [SiteA, SiteB])
            .Allowed.Should().BeTrue();
    }

    [Fact]
    public void DeviceWritingToAnotherSite_IsForbidden()
    {
        // Chính kịch bản trong issue: Site B có thật, nên trước đây yêu cầu đi lọt và trả 201.
        var decision = IotSiteAccessGuard.Check(SiteA, [SiteB], [SiteA, SiteB]);

        decision.Allowed.Should().BeFalse();
        decision.StatusCode.Should().Be(403);
    }

    [Fact]
    public void ABatchMixingOwnAndForeignSites_IsRejectedWhole()
    {
        // Nhận phần "của mình" rồi bỏ phần còn lại sẽ khiến người gọi tưởng cả lô đã ghi, và dữ liệu
        // an toàn thiếu mẫu mà không ai biết.
        var decision = IotSiteAccessGuard.Check(SiteA, [SiteA, SiteB], [SiteA, SiteB]);

        decision.Allowed.Should().BeFalse();
        decision.StatusCode.Should().Be(403);
    }

    [Fact]
    public void UnknownSite_IsAControlled404_NotADatabaseError()
    {
        // Trước đây GUID không tồn tại đi thẳng xuống DB và nổ lỗi khoá ngoại → 500. Đó là lỗi của
        // người gọi, phải nói rõ bằng 4xx.
        var decision = IotSiteAccessGuard.Check(deviceSiteId: null, [Ghost], [SiteA]);

        decision.Allowed.Should().BeFalse();
        decision.StatusCode.Should().Be(404);
    }

    [Fact]
    public void ForeignAndNonExistentSite_IsReportedAsForbidden_NotAsNotFound()
    {
        // Thứ tự kiểm là có chủ ý. Trả 404 ở đây sẽ cho thiết bị một cách dò xem site nào có thật —
        // so 404 với 403 là biết ngay — tức biến chính hàng rào này thành công cụ do thám.
        var decision = IotSiteAccessGuard.Check(SiteA, [Ghost], [SiteA]);

        decision.Allowed.Should().BeFalse();
        decision.StatusCode.Should().Be(403);
    }

    [Fact]
    public void HumanCallerWithoutADeviceClaim_IsOnlyCheckedForExistence()
    {
        // Staff dùng JWT tạo sự cố thủ công (NS-23) không có claim iot:site_id; quyền của họ do tầng
        // JWT lo. Áp luật thiết bị lên họ sẽ chặn nhầm đúng người cần tạo cảnh báo cháy.
        IotSiteAccessGuard.Check(deviceSiteId: null, [SiteB], [SiteA, SiteB])
            .Allowed.Should().BeTrue();
    }

    [Fact]
    public void EmptyRequest_IsAllowed()
    {
        // Lô rỗng đã bị ValidateAsync chặn trước đó; guard không được tự ý báo lỗi khác.
        IotSiteAccessGuard.Check(SiteA, [], [SiteA]).Allowed.Should().BeTrue();
    }

    [Fact]
    public void DuplicateSiteIdsInOneBatch_AreHandledOnce()
    {
        IotSiteAccessGuard.Check(SiteA, [SiteA, SiteA, SiteA], [SiteA])
            .Allowed.Should().BeTrue();
    }
}
