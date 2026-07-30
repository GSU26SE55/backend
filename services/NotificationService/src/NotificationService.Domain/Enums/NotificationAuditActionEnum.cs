namespace NotificationService.Domain.Enums;

/// <summary>
/// Action audit của NotificationService — 7 action gốc (#AUDIT-34) + 1 bổ sung Sprint 6.3.
/// Enum bắt đầu từ 1.
/// </summary>
public enum NotificationAuditActionEnum
{
    PushSent = 1,
    PushFailed = 2,
    PushDelivered = 3,
    PushOpened = 4,
    InAppCreated = 5,
    InAppRead = 6,
    InAppDismissed = 7,

    /// <summary>Sprint 6.3 NOTI3-12 (#712) — admin gửi thử một template email.</summary>
    TemplateTestSent = 8,
}
