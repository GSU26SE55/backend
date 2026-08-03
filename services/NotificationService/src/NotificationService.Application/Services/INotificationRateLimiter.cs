using NotificationService.Domain.Enums;

namespace NotificationService.Application.Services;

/// <summary>Sprint 6.3 NOTI3-06 (#706) — kết quả kiểm tra hạn mức.</summary>
/// <param name="Allowed"><c>true</c> = được gửi ngay.</param>
/// <param name="Reason">Hạn mức nào bị chạm (<c>per_hour</c> / <c>per_type</c>) — dùng làm label metric.</param>
public readonly record struct RateLimitDecision(bool Allowed, string? Reason = null)
{
    public static readonly RateLimitDecision Allow = new(true);
}

/// <summary>
/// Sprint 6.3 NOTI3-06 (#706) — hạn mức notification chủ động theo người dùng.
///
/// Hiện thực dùng Redis, đếm theo cửa sổ trượt xấp xỉ (xem <c>NotificationRateLimiter</c>).
/// Notification thuộc <c>CriticalTypes</c> **luôn được bỏ qua hạn mức** — hạn mức tồn tại để bảo vệ
/// sự chú ý của người dùng, không phải để chặn cảnh báo an toàn.
/// </summary>
public interface INotificationRateLimiter
{
    /// <summary>
    /// Ghi nhận một lần gửi và cho biết có vượt hạn mức không.
    /// Gọi đúng MỘT lần cho mỗi notification ngay trước khi giao xuống channel — gọi lại sẽ đếm trùng.
    /// </summary>
    Task<RateLimitDecision> TryConsumeAsync(
        Guid userId, NotificationTypeEnum type, CancellationToken ct = default);
}
