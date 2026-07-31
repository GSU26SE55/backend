using SharedContracts.Events.Root;

namespace SharedContracts.Events;

/// <summary>
/// Publish khi NotificationService muốn gửi email notification chung (ticket alert, SLA breach, ...).
/// EmailService consume và gửi email thực tế — template rendering xử lý ở #111.
/// </summary>
/// <param name="NotificationId"></param>
/// <param name="ToEmail"></param>
/// <param name="Subject"></param>
/// <param name="Body"></param>
/// <param name="SourceService"></param>
/// <param name="UnsubscribeUrl">
/// Sprint 6.3 NOTI3-15 (#715) — URL hủy đăng ký một chạm cho email KHÔNG giao dịch.
///
/// Có giá trị ⇒ EmailService gắn <c>List-Unsubscribe</c> +
/// <c>List-Unsubscribe-Post: List-Unsubscribe=One-Click</c> (RFC 8058). Gmail/Yahoo yêu cầu hủy một
/// chạm với người gửi số lượng lớn từ 2024; thiếu nó, người nhận không tìm thấy nút hủy sẽ bấm
/// "báo cáo spam" — và tỷ lệ spam vượt 0.3% là mất reputation domain.
///
/// <c>null</c> ⇒ email giao dịch (OTP, đặt lại mật khẩu, mời admin): KHÔNG được gắn, vì người dùng
/// không thể "hủy đăng ký" khỏi mã xác thực do chính họ yêu cầu.
/// </param>
public record SendNotificationEmailEvent(
    Guid NotificationId,
    string ToEmail,
    string Subject,
    string Body,
    string SourceService = "notification",
    string? UnsubscribeUrl = null
) : IntegrationEvent;
