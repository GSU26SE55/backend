using MassTransit;

namespace TicketService.Infrastructure.Sagas;

/// <summary>
/// Saga state instance cho 1 Alert. Persisted bởi MassTransit.EntityFrameworkCore.
///
/// Lifecycle: <c>Initial</c> → <c>TicketRequested</c> → <c>TicketProvisioned</c>
/// → <c>AlertLinkRequested</c> → <c>Completed</c>.
///
/// Failed (terminal): bất kỳ stage nào nhận rejection / bounded retry exhausted.
///
/// Hàng <c>Completed</c>/<c>Failed</c> KHÔNG xóa — giữ làm tombstone cho audit + duplicate dedup.
///
/// Sprint 5B #237 (xem overall.md §53.8).
/// </summary>
public class AlertTicketSagaState : SagaStateMachineInstance, ISagaVersion
{
    /// <summary>
    /// CorrelationId = AlertId (per overall.md §53.7 — explicit AlertId correlation).
    /// </summary>
    public Guid CorrelationId { get; set; }

    /// <summary>
    /// State name (matching <c>State.Name</c> trong AlertTicketSagaStateMachine).
    /// MassTransit stores state as string by default.
    /// </summary>
    public string CurrentState { get; set; } = null!;

    /// <summary>Version — MassTransit ISagaVersion. EF uses PostgreSQL xmin for optimistic concurrency.</summary>
    public int Version { get; set; }

    // ===== Snapshot từ BatteryAnomalyDetected event (V1/V2) =====
    public Guid AlertId { get; set; }
    public Guid? BatteryAssetId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid? SiteId { get; set; }
    public string? AssetSerialNumber { get; set; }
    public int AnomalyType { get; set; }
    public int Severity { get; set; }
    public decimal? ThresholdValue { get; set; }
    public decimal? ActualValue { get; set; }
    public string? Unit { get; set; }
    public DateTime DetectedAt { get; set; }

    // ===== Tier 2 / Environmental scope (V2 only) =====
    public decimal? InternalResistanceMilliohm { get; set; }
    public decimal? CellVoltageDeltaMv { get; set; }
    public Guid? EnvironmentalIncidentId { get; set; }

    // ===== BE-AI — prescription text (V2 only, chỉ từ SohPredictionBackgroundService) =====
    public string? AiPrescription { get; set; }

    /// <summary>
    /// BE-AI structured — payload JSON của <see cref="AiSuggestionPayload"/>, forward nguyên
    /// vẹn từ event V2 sang <c>CreateTicketFromAlertCommand</c>.
    /// </summary>
    /// <remarks>
    /// Cố ý gom vào MỘT cột JSON thay vì rải 12 cột: saga state chỉ là kho tạm giữa hai chặng,
    /// không ai truy vấn theo từng mục ở đây (nơi truy vấn là bảng <c>ticket_ai_suggestions</c>
    /// của TicketService). Rải cột chỉ làm bảng saga phình mà không thêm khả năng nào.
    /// Null với ticket từ threshold engine (không có AI).
    /// </remarks>
    public string? AiSuggestionJson { get; set; }

    // ===== Ticket info — set sau khi TicketCreated response =====
    public Guid? TicketId { get; set; }
    public string? TicketCode { get; set; }
    public bool TicketIsReused { get; set; }

    // ===== Failure tracking =====
    public string? FailedAtStage { get; set; }
    public string? FailureReason { get; set; }
    public string? FailureErrorCode { get; set; }
    public DateTime? FailedAt { get; set; }
    public int RetryCount { get; set; }

    // ===== Timing =====
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    // ===== Quartz scheduled timeout ids — cancel khi tiến bước =====
    public Guid? PendingTimeoutTokenId { get; set; }
}
