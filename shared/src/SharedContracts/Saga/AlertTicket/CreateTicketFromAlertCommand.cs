using SharedContracts.Events.Root;

namespace SharedContracts.Saga.AlertTicket;

/// <summary>
/// Saga → TicketService: create (or reuse) Ticket cho Alert.
/// Sent bởi <c>AlertTicketSagaStateMachine</c> sau khi nhận
/// <see cref="SharedContracts.Events.BatteryAnomalyDetectedEvent"/> hoặc V2.
///
/// Sprint 5B #236 — Saga contracts (xem overall.md §53.7).
/// </summary>
public record CreateTicketFromAlertCommand(
    Guid CorrelationId,
    Guid AlertId,
    Guid BatteryAssetId,
    Guid CustomerId,
    string AssetSerialNumber,
    int AnomalyType,
    int Severity,
    decimal? ThresholdValue,   // §1.3.5 — nullable cho incident-based alert
    decimal? ActualValue,
    string? Unit,
    DateTime DetectedAt,
    string AnomalyCategory,
    string Title,
    string Description
) : IntegrationEvent;
