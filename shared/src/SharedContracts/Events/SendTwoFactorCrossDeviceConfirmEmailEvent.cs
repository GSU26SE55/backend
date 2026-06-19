using SharedContracts.Events.Root;

namespace SharedContracts.Events;

/// <summary>
/// #AUTH-51: Event được publish bởi AuthService khi user request setup 2FA xuyên thiết bị.
/// EmailService consume → render template + gửi email chứa confirm link.
/// </summary>
/// <param name="ToEmail">Email của user.</param>
/// <param name="FullName">Tên hiển thị để personalize email.</param>
/// <param name="ConfirmUrl">URL đầy đủ FE phải redirect user khi click — chứa token query param.</param>
/// <param name="ExpiresInMinutes">TTL còn lại của token, hiển thị trong email.</param>
public record SendTwoFactorCrossDeviceConfirmEmailEvent(
    string ToEmail,
    string FullName,
    string ConfirmUrl,
    int ExpiresInMinutes
) : IntegrationEvent;
