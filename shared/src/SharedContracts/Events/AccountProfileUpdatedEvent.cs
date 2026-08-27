using SharedContracts.Events.Root;

namespace SharedContracts.Events;

/// <summary>
/// Publish khi profile account thay đổi (FullName, PhoneNumber, AvatarUrl, ...).
/// KHÔNG publish cho thay đổi credential (password, 2FA) hoặc role assignment — có event riêng.
///
/// Subscribers tiêu biểu:
/// - BatteryService / TicketService: sync display name của Customer/Staff trong cached list.
/// - NotificationService: cập nhật contact info cho push token.
/// </summary>
public record AccountProfileUpdatedEvent(
    Guid AccountId,
    string Email,
    string FullName,
    string? PhoneNumber,
    string? AvatarUrl,
    string Role = "",
    int AccountStatus = 0,
    bool IsActive = false
) : IntegrationEvent;
