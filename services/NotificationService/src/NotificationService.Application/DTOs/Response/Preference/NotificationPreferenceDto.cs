namespace NotificationService.Application.DTOs.Response.Preference;

public class NotificationPreferenceDto
{
    public bool PushEnabled { get; set; }
    public bool EmailEnabled { get; set; }
    public bool SmsEnabled { get; set; }
    public bool InAppEnabled { get; set; }
    public string? QuietHoursStart { get; set; }  // "HH:mm" or null
    public string? QuietHoursEnd { get; set; }    // "HH:mm" or null
    public string TimeZone { get; set; } = "Asia/Ho_Chi_Minh";
}
