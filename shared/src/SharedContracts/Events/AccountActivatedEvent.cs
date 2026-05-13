using SharedContracts.Events.Root;

namespace SharedContracts.Events;

/// <summary>
/// Publish khi account chuyển sang trạng thái <c>Active</c> và sẵn sàng sử dụng.
///
/// Trigger:
/// - User verify OTP đăng ký thành công (qua RegisterCommand → VerifyOtpCommand).
/// - Admin tạo account trực tiếp với Status=Active (qua admin CreateAccountCommand).
/// - Google OAuth auto-create account lần đầu.
///
/// Subscribers tiêu biểu:
/// - NotificationService: gửi welcome email.
/// - Các service khác cần sync user (BatteryService, TicketService).
/// </summary>
public record AccountActivatedEvent(
    Guid AccountId,
    string Email,
    string FullName,
    string? PhoneNumber,
    IReadOnlyList<string> Roles,
    string CreationSource
) : IntegrationEvent;
