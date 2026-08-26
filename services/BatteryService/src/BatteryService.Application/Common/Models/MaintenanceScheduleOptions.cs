namespace BatteryService.Application.Common.Models;

/// <summary>
/// Cấu hình lịch bảo trì định kỳ ở tầng tài sản.
/// </summary>
/// <remarks>
/// <see cref="DefaultCycleMonths"/> chỉ là giá trị dự phòng khi
/// <c>BatteryType.MaintenanceIntervalMonths</c> để trống — chu kỳ thật lấy theo loại pin.
/// </remarks>
public class MaintenanceScheduleOptions
{
    public const string SectionName = "Battery:MaintenanceSchedule";

    public bool Enabled { get; set; } = true;

    public string TimeZoneId { get; set; } = "Asia/Ho_Chi_Minh";

    /// <summary>Chu kỳ dự phòng (tháng) khi loại pin không khai báo.</summary>
    public int DefaultCycleMonths { get; set; } = 6;

    /// <summary>Số ngày mở ticket trước hạn.</summary>
    public int LeadDays { get; set; } = 7;

    /// <summary>Cửa sổ cho Customer chọn lịch khi kỳ đã quá hạn lúc mở ticket.</summary>
    public int OverdueScheduleWindowDays { get; set; } = 7;

    public int PollIntervalSeconds { get; set; } = 60;

    public int BatchSize { get; set; } = 100;
}
