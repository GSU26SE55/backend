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
    string Description,
    // BE-AI structured — forward từ BatteryAnomalyDetectedV2Event để TicketService lưu vào
    // `ticket_ai_suggestions`. `Description` ở trên VẪN chứa đoạn "--- AI Prescription ---"
    // (text ghép chuỗi) như cũ — các field này là BỔ SUNG, không thay thế.
    //
    // Nullable + CUỐI constructor: saga đã deploy publish command không có field này vẫn
    // deserialize được ở consumer mới (cùng quy ước với BatteryAnomalyDetectedV2Event).
    string? AiPrescription = null,
    IReadOnlyList<string>? AiActionSteps = null,
    IReadOnlyList<string>? AiPpeRequired = null,
    IReadOnlyList<string>? AiSopReferences = null,
    IReadOnlyList<string>? AiEscalationConditions = null,
    IReadOnlyList<string>? AiSafetyWarnings = null,
    IReadOnlyList<string>? AiKbDocRefs = null,
    bool? AiHumanVerificationRequired = null,
    bool? AiBlocked = null,
    bool? AiEnriched = null,
    string? AiLlmProvider = null,
    string? AiPrescriptionId = null
) : IntegrationEvent;
