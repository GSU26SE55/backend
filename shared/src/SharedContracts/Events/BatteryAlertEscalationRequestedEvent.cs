using SharedContracts.Events.Root;

namespace SharedContracts.Events;

/// <summary>
/// BatteryService → NotificationService: push Manager khi Critical Alert chưa-ack
/// trong vòng 5 phút (escalation timer).
///
/// **Tách khỏi <see cref="BatteryAnomalyDetectedEvent"/>** ở Sprint 5B #238 để
/// Saga-start event KHÔNG bị republish khi alert escalate. Notification path
/// dùng event này, Saga path dùng V1/V2 anomaly event.
///
/// Subscriber:
/// - NotificationService: <c>BatteryAlertEscalationRequestedConsumer</c>.
///   Push Manager + Admin notification (push/email) — kèm debounce 5 phút (§49.2).
///
/// Sprint 5B #238 (xem overall.md §53.7).
/// </summary>
public record BatteryAlertEscalationRequestedEvent(
    Guid AlertId,
    Guid BatteryAssetId,
    Guid CustomerId,
    string AssetSerialNumber,
    int AnomalyType,
    int Severity,
    decimal? ActualValue,   // §1.3.5 — nullable cho incident-based alert
    string? Unit,           // §1.3.5 — nullable cho incident-based alert
    DateTime DetectedAt,
    DateTime EscalationRequestedAt,
    int MinutesSinceDetection
) : IntegrationEvent;
