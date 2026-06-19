namespace SmsService.Domain.Enums;

/// <summary>
/// State machine cho 1 SMS trong gateway:
/// <code>
/// Pending  → Sending → Sent
/// Pending  → Sending → Failed (retry &lt; max → quay về Pending; ≥ max → Failed final)
/// Pending  → Cancelled (admin huỷ thủ công)
/// Sending  → Pending (StaleSmsReaper revert sau 5 phút)
/// </code>
/// </summary>
public enum SmsStatus
{
    Pending = 0,
    Sending = 1,
    Sent = 2,
    Failed = 3,
    Cancelled = 4
}
