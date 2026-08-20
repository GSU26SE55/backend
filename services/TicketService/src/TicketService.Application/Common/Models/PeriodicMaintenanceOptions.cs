namespace TicketService.Application.Common.Models;

public class PeriodicMaintenanceOptions
{
    public const string SectionName = "Ticket:PeriodicMaintenance";

    public bool Enabled { get; set; } = true;
    public string TimeZoneId { get; set; } = "Asia/Ho_Chi_Minh";
    public int CycleMonths { get; set; } = 6;
    public int LeadDays { get; set; } = 7;
    public int OverdueScheduleWindowDays { get; set; } = 7;
    public TimeSpan ReminderTime { get; set; } = TimeSpan.FromHours(8);
    public int PollIntervalSeconds { get; set; } = 60;
    public int BatchSize { get; set; } = 100;
}
