namespace TicketService.Application.Common.Models;

/// <summary>
/// Cấu hình việc nhắc khách chọn giờ cho ticket bảo trì định kỳ.
/// </summary>
/// <remarks>
/// Không còn <c>CycleMonths</c> và <c>LeadDays</c>: độ dài chu kỳ và thời điểm mở ticket nay
/// thuộc về BatteryService — chu kỳ lấy theo <c>BatteryType.MaintenanceIntervalMonths</c>, mỗi
/// loại pin một nhịp. TicketService chỉ còn lo phần sau khi ticket đã mở.
/// </remarks>
public class PeriodicMaintenanceOptions
{
    public const string SectionName = "Ticket:PeriodicMaintenance";

    public bool Enabled { get; set; } = true;
    public string TimeZoneId { get; set; } = "Asia/Ho_Chi_Minh";
    public int OverdueScheduleWindowDays { get; set; } = 7;
    public TimeSpan ReminderTime { get; set; } = TimeSpan.FromHours(8);
    public int PollIntervalSeconds { get; set; } = 60;
    public int BatchSize { get; set; } = 100;
}
