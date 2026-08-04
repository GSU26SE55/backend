using SharedContracts.Events.Root;

namespace SharedContracts.Events;

/// <summary>
/// Publish khi BatteryService phát hiện bất thường Critical trên pin.
///
/// Trigger: <c>ThresholdAnomalyDetector</c> phát hiện reading vượt ngưỡng severity Critical →
/// <c>AlertOrchestrator</c> tạo Alert mới (không phải dedup merge) → ghi Outbox →
/// <c>OutboxRelayBackgroundService</c> publish.
///
/// Subscribers:
/// - TicketService: auto-create ticket (BR-02) — Sprint 4.
/// - NotificationService: push notification + email Manager — Sprint 6.
///
/// Lưu ý:
/// - Warning severity KHÔNG publish event (chỉ ghi alert) — tránh spam ticket.
/// - Merged alert KHÔNG publish event (đã có alert gốc Open) — tránh duplicate ticket.
/// </summary>
public record BatteryAnomalyDetectedEvent(
    Guid AlertId,
    Guid BatteryAssetId,
    Guid CustomerId,
    string AssetSerialNumber,
    int AnomalyType,           // AnomalyTypeEnum value
    int Severity,              // AlertSeverityEnum value
    decimal? ThresholdValue,   // §1.3.5 — nullable cho incident-based alert
    decimal? ActualValue,      // §1.3.5 — nullable cho incident-based alert
    string? Unit,              // §1.3.5 — nullable cho incident-based alert
    DateTime DetectedAt,

    // 03/08/2026 — thêm tên enum kèm theo số, đúng khuôn OldStatusName/NewStatusName của
    // TicketStatusChangedEvent.
    //
    // Vì sao cần: hai enum trên thuộc BatteryService.Domain nên subscriber KHÔNG tham chiếu được,
    // chỉ nhận con số trần. NotificationService vì thế gửi cho khách những câu như
    // "Loại: 4 — Mức độ: 3". Tự dựng bảng tra số ở phía nhận thì mỗi lần BatteryService thêm loại
    // bất thường mới, phía nhận lại âm thầm hiện ra số — đúng kiểu trôi lệch mà không ai hay.
    // Bên sở hữu enum gửi kèm tên là chỗ duy nhất luôn đúng.
    //
    // Nullable + mặc định null: event cũ đã nằm trong Outbox/hàng đợi không có hai trường này,
    // deserialize ra null và phía nhận tự lùi về số.
    string? AnomalyTypeName = null,   // nameof(AnomalyTypeEnum.X)
    string? SeverityName = null       // nameof(AlertSeverityEnum.X)
) : IntegrationEvent;
