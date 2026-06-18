using SharedContracts.Events.Root;

namespace SharedContracts.Events;

/// <summary>
/// #AUTH-52: phát hiện login từ IP hoặc User-Agent CHƯA TỪNG thấy ở account này.
/// NotificationService consume để email user "có thiết bị mới đăng nhập".
/// </summary>
public record SuspiciousLoginDetectedEvent(
    Guid AccountId,
    string Email,
    string? IpAddress,
    string? UserAgent,
    string Reason,            // "new_ip" | "new_user_agent" | "new_ip_and_user_agent"
    DateTime DetectedAt
) : IntegrationEvent;
