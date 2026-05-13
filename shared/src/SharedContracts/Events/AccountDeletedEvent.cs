using SharedContracts.Events.Root;

namespace SharedContracts.Events;

/// <summary>
/// Publish khi account bị xóa mềm (soft delete) khỏi AuthService.
///
/// Trigger:
/// - Admin xóa account (admin DeleteAccountCommand).
/// - User tự xóa account (DeleteMeCommand).
///
/// Subscribers tiêu biểu:
/// - BatteryService / TicketService: đánh dấu account là deleted, unassign battery/ticket.
/// - NotificationService: xóa push token, unsubscribe khỏi topic.
///
/// Lưu ý: account chỉ soft-delete, dữ liệu vẫn còn trong AuthService DB cho audit.
/// </summary>
public record AccountDeletedEvent(
    Guid AccountId,
    string Email,
    string DeletionSource
) : IntegrationEvent;
