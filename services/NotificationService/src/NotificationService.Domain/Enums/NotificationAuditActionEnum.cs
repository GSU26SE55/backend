namespace NotificationService.Domain.Enums;

/// <summary>7 action audit của NotificationService (Sprint audit #AUDIT-34). Enum bắt đầu từ 1.</summary>
public enum NotificationAuditActionEnum
{
    PushSent = 1,
    PushFailed = 2,
    PushDelivered = 3,
    PushOpened = 4,
    InAppCreated = 5,
    InAppRead = 6,
    InAppDismissed = 7,
}
