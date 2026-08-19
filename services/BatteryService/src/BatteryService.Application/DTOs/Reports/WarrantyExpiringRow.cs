namespace BatteryService.Application.DTOs.Reports;

/// <summary>Sprint 7 #114 (§5.2) — asset sắp hết bảo hành.</summary>
public class WarrantyExpiringRow
{
    public string AssetId { get; set; } = string.Empty;
    public string? SerialNumber { get; set; }
    public DateTime? WarrantyEndDate { get; set; }
    public int? DaysRemaining { get; set; }
    public string CustomerId { get; set; } = string.Empty;

    /// <summary>
    /// Tên khách hàng, lấy từ read-model <c>CustomerAccount</c>.
    ///
    /// <para>Báo cáo này hiển thị cột "Customer" cho người đọc, mà trước đây chỉ có
    /// <see cref="CustomerId"/> nên FE in thẳng GUID ra màn hình. Hàng bên cạnh đã có
    /// <see cref="SerialNumber"/> cho asset — cột customer chỉ đang thiếu phần tương ứng.</para>
    ///
    /// <para><c>null</c> khi tài khoản đã bị xoá hoặc read-model chưa kịp đồng bộ; FE lùi về
    /// <see cref="CustomerId"/> chứ không để trống ô.</para>
    /// </summary>
    public string? CustomerName { get; set; }
}
