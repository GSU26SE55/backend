namespace BatteryService.Application.Helpers;

/// <summary>Kết quả kiểm quyền ghi dữ liệu theo site cho thiết bị IoT.</summary>
/// <param name="Allowed">Cho phép hay không.</param>
/// <param name="StatusCode">Mã trạng thái khi bị chặn (403 hoặc 404).</param>
/// <param name="Message">Thông báo kèm theo khi bị chặn.</param>
public readonly record struct SiteAccessDecision(bool Allowed, int StatusCode, string? Message)
{
    public static readonly SiteAccessDecision Ok = new(true, 0, null);
}

/// <summary>
/// GH-806 — thiết bị IoT chỉ được ghi dữ liệu cho ĐÚNG site của nó.
/// </summary>
/// <remarks>
/// <para>
/// Ambient reading và environmental incident trước đây lấy thẳng <c>SiteId</c> từ body: một thiết bị
/// thuộc Site A gửi dữ liệu cho Site B vẫn nhận 201. Nghĩa là một thiết bị bị chiếm quyền có thể đầu
/// độc dữ liệu an toàn (khói, gas, ngập) của khách hàng khác, hoặc tạo sự cố giả cho họ.
/// Đường sensor ingest đã có hàng rào này từ #IoT2-18; hai đường kia thì chưa.
/// </para>
/// <para>
/// <b>Thứ tự kiểm là có chủ ý: quyền TRƯỚC, tồn tại SAU.</b> Kiểm tồn tại trước thì thiết bị có thể
/// dò xem site nào có thật bằng cách so 404 với 403 — biến chính hàng rào này thành công cụ do thám.
/// </para>
/// </remarks>
public static class IotSiteAccessGuard
{
    /// <param name="deviceSiteId">
    /// Site của thiết bị đã xác thực, lấy từ claim <c>iot:site_id</c>. <c>null</c> nghĩa là người gọi
    /// KHÔNG phải thiết bị (ví dụ Staff dùng JWT) — lúc đó chỉ còn kiểm tồn tại.
    /// </param>
    /// <param name="requestedSiteIds">Các site mà yêu cầu muốn ghi vào.</param>
    /// <param name="existingSiteIds">Các site có thật trong cơ sở dữ liệu.</param>
    public static SiteAccessDecision Check(
        Guid? deviceSiteId,
        IReadOnlyCollection<Guid> requestedSiteIds,
        IReadOnlyCollection<Guid> existingSiteIds)
    {
        ArgumentNullException.ThrowIfNull(requestedSiteIds);
        ArgumentNullException.ThrowIfNull(existingSiteIds);

        if (deviceSiteId.HasValue)
        {
            var foreign = requestedSiteIds.Where(id => id != deviceSiteId.Value).Distinct().ToList();
            if (foreign.Count > 0)
            {
                return new SiteAccessDecision(false, 403,
                    "The device does not have permission to write data for another site.");
            }
        }

        var missing = requestedSiteIds.Where(id => !existingSiteIds.Contains(id)).Distinct().ToList();
        if (missing.Count > 0)
        {
            // Trước đây site không tồn tại đi thẳng xuống DB và nổ ra lỗi khoá ngoại → 500. Đó là lỗi
            // của người gọi, phải trả về 4xx nói rõ, không phải lỗi máy chủ.
            return new SiteAccessDecision(false, 404, "Site not found.");
        }

        return SiteAccessDecision.Ok;
    }
}
