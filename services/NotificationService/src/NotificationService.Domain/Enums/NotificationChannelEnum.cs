namespace NotificationService.Domain.Enums;

/// <summary>
/// Kênh gửi notification. §3.3 overall.md.
/// </summary>
public enum NotificationChannelEnum
{
    Push = 1,
    Email = 2,
    Sms = 3,
    InApp = 4
}
