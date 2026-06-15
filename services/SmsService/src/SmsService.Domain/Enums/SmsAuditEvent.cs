namespace SmsService.Domain.Enums;

/// <summary>
/// Loại sự kiện ghi vào append-only audit log <c>sms_audit_logs</c>.
/// </summary>
public enum SmsAuditEvent
{
    Queued = 0,
    Picked = 1,
    Sent = 2,
    Failed = 3,
    Retry = 4,
    Cancelled = 5,
    Reaped = 6,
    Redacted = 7
}
