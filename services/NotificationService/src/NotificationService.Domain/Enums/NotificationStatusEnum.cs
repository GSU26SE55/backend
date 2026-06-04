namespace NotificationService.Domain.Enums;

/// <summary>
/// Trạng thái lifecycle của notification record. §3.3 overall.md.
/// </summary>
public enum NotificationStatusEnum
{
    Pending = 1,
    Sent = 2,
    Failed = 3,
    Read = 4
}
