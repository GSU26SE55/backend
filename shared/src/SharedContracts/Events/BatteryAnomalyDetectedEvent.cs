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
    decimal ThresholdValue,
    decimal ActualValue,
    string Unit,
    DateTime DetectedAt
) : IntegrationEvent;
