namespace NotificationService.Domain.Enums;

/// <summary>
/// Tần suất gửi notification (per-user preference) — Immediate vs Daily digest.
/// Bám §49 (Notification advanced — digest).
/// </summary>
public enum NotificationFrequencyEnum
{
    Immediate = 1,
    Daily = 2
}
