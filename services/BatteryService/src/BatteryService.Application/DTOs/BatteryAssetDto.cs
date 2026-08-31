using BatteryService.Domain.Enums;

namespace BatteryService.Application.DTOs;

public class BatteryAssetDto
{
    /// <summary>Định danh resource.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Serial number của asset (unique).</summary>
    public string SerialNumber { get; set; } = string.Empty;

    /// <summary>ID BatteryType (Guid).</summary>
    public string BatteryTypeId { get; set; } = string.Empty;

    /// <summary>Tên của batterytype.</summary>
    public string BatteryTypeName { get; set; } = string.Empty;

    /// <summary>ID Site (Guid).</summary>
    public string? SiteId { get; set; }

    /// <summary>Tên của site.</summary>
    public string? SiteName { get; set; }

    /// <summary>ID Customer (Guid).</summary>
    public string CustomerId { get; set; } = string.Empty;

    /// <summary>Tên của customer.</summary>
    public string CustomerName { get; set; } = string.Empty;

    /// <summary>Ngày lắp đặt.</summary>
    public DateTime InstallDate { get; set; }

    /// <summary>Ngày hết bảo hành.</summary>
    public DateTime? WarrantyEndDate { get; set; }

    /// <summary>Trạng thái bảo hành (Active | Expired).</summary>
    public WarrantyStatusEnum WarrantyStatus { get; set; }

    /// <summary>Vị trí lắp đặt (vd "Block A - Rack 01").</summary>
    public string? Location { get; set; }

    /// <summary>Vĩ độ (-90..90).</summary>
    public decimal? Latitude { get; set; }

    /// <summary>Kinh độ (-180..180).</summary>
    public decimal? Longitude { get; set; }

    /// <summary>Filter theo status enum.</summary>
    public BatteryStatusEnum Status { get; set; }

    /// <summary>Ghi chú tự do.</summary>
    public string? Notes { get; set; }

    /// <summary>Timestamp reading gần nhất cho asset.</summary>
    public DateTime? LastSensorReadingAt { get; set; }

    /// <summary>
    /// 1 nếu asset có alert đang ở trạng thái Open hoặc Acknowledged, ngược lại 0. Cùng logic
    /// AssetsWithActiveAlerts của SiteDashboardDto — không phải tổng số alert record — để hai
    /// con số khớp nhau trên UI.
    /// </summary>
    public int ActiveAlertCount { get; set; }

    /// <summary>Điểm rủi ro lan truyền cascade — cùng field với CascadeRiskDto.</summary>
    public decimal CascadeRiskScore { get; set; }

    /// <summary>Mức rủi ro suy ra từ CascadeRiskScore (CascadeRiskDto.ToLevel).</summary>
    public CascadeRiskLevel CascadeRiskLevel { get; set; }

    /// <summary>Lần bảo trì định kỳ gần nhất đã hoàn tất. Null = chưa lần nào.</summary>
    public DateTime? LastMaintenanceAtUtc { get; set; }

    /// <summary>Kỳ bảo trì kế tiếp. Luôn có giá trị — pin chưa bảo trì thì tính từ InstallDate.</summary>
    public DateTime NextMaintenanceDueAtUtc { get; set; }

    /// <summary>Số thứ tự kỳ kế tiếp — 1 là kỳ đầu kể từ khi lắp đặt.</summary>
    public int MaintenanceCycleNo { get; set; }

    /// <summary>Chu kỳ (tháng) đang áp dụng — theo loại pin, thiếu thì lấy mặc định hệ thống.</summary>
    public int MaintenanceIntervalMonths { get; set; }

    /// <summary>Timestamp tạo (UTC).</summary>
    public DateTime CreatedAt { get; set; }
}
