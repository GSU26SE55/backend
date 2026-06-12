using SharedContracts.Events.Root;

namespace SharedContracts.Saga.AlertTicket;

/// <summary>
/// Admin → Saga: reconcile auto-Ticket cũ có <c>OriginAlertId</c> nhưng
/// <c>Alert.TicketId</c> chưa link (legacy data từ trước Sprint 5B).
///
/// Sent qua admin endpoint <c>POST /api/v1/admin/sagas/alert-ticket/reconcile</c>
/// (#239). Khác với reprocess: reconcile tạo Saga state machine mới từ Ticket
/// đã tồn tại, không tạo Ticket mới.
///
/// Sprint 5B #236 — Saga contracts.
/// </summary>
public record AlertTicketReconciliationCommand(
    Guid CorrelationId,
    Guid AlertId,
    Guid TicketId,
    Guid BatteryAssetId,
    Guid CustomerId,
    string AssetSerialNumber,
    string TicketCode,
    string AnomalyCategory,
    DateTime DetectedAt
) : IntegrationEvent;
