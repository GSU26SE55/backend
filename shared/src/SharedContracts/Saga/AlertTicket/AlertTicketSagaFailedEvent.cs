using SharedContracts.Events.Root;

namespace SharedContracts.Saga.AlertTicket;

/// <summary>
/// Saga → World: terminal failure event publish khi Saga vào state Failed.
///
/// Subscribers:
/// - NotificationService: notify Admin/Manager về Saga Failed cần intervention
///   (<c>AlertTicketSagaFailedConsumer</c> — xem #238).
/// - Observability: increment metric <c>saga_alert_ticket_failed_total{reason}</c>.
///
/// Sprint 5B #236 — Saga contracts (xem overall.md §53.11).
/// </summary>
public record AlertTicketSagaFailedEvent(
    Guid CorrelationId,
    Guid AlertId,
    Guid? TicketId,
    Guid BatteryAssetId,
    Guid CustomerId,
    string AssetSerialNumber,
    string FailedAtStage,
    string Reason,
    string? ErrorCode,
    DateTime FailedAt
) : IntegrationEvent;
