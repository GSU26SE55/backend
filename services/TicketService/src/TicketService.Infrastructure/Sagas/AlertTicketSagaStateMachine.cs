using System.Text.Json;
using MassTransit;
using Microsoft.Extensions.Logging;
using SharedContracts.Events;
using SharedContracts.Saga.AlertTicket;
using SharedInfrastructure.Metrics;

namespace TicketService.Infrastructure.Sagas;

/// <summary>
/// State machine cho Alert→Ticket Saga.
///
/// Subscribe cả V1 (<see cref="BatteryAnomalyDetectedEvent"/>) và V2
/// (<see cref="BatteryAnomalyDetectedV2Event"/>) — overall.md §30.6.
///
/// Lifecycle:
///   Initial → TicketRequested → TicketProvisioned → AlertLinkRequested → Completed
///           ↘ Failed (terminal)                  ↘ Failed (terminal)
///
/// Optimistic concurrency: PostgreSQL <c>xmin</c> (xem AlertTicketSagaStateConfiguration).
/// Persistent timeout: Quartz scheduler (xem #235 AddQuartzPersistenceSchema migration).
///
/// Sprint 5B #237 (xem overall.md §53.8, §18.2bis PR review checklist 9 mục).
/// </summary>
public class AlertTicketSagaStateMachine : MassTransitStateMachine<AlertTicketSagaState>
{
    private const int MaxRetryCount = 3;
    private static readonly TimeSpan TicketCreationTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan AlertLinkTimeout = TimeSpan.FromMinutes(5);

    public State TicketRequested { get; private set; } = null!;
    public State TicketProvisioned { get; private set; } = null!;
    public State AlertLinkRequested { get; private set; } = null!;
    public State Completed { get; private set; } = null!;
    public State Failed { get; private set; } = null!;

    // V1 / V2 anomaly events (start triggers).
    public Event<BatteryAnomalyDetectedEvent> AnomalyDetectedV1 { get; private set; } = null!;
    public Event<BatteryAnomalyDetectedV2Event> AnomalyDetectedV2 { get; private set; } = null!;

    // Ticket-side responses.
    public Event<TicketCreatedFromAlertResponse> TicketCreated { get; private set; } = null!;
    public Event<TicketCreationFromAlertRejected> TicketCreationRejected { get; private set; } = null!;

    // Alert-side responses.
    public Event<AlertLinkedToTicketResponse> AlertLinked { get; private set; } = null!;
    public Event<AlertLinkToTicketRejected> AlertLinkRejected { get; private set; } = null!;

    // Reconciliation (admin).
    public Event<AlertTicketReconciliationCommand> ReconciliationRequested { get; private set; } = null!;

    // Quartz scheduled timeouts.
    public Schedule<AlertTicketSagaState, TicketCreationTimeoutFired> TicketCreationTimer { get; private set; } = null!;
    public Schedule<AlertTicketSagaState, AlertLinkTimeoutFired> AlertLinkTimer { get; private set; } = null!;

    public AlertTicketSagaStateMachine(ILogger<AlertTicketSagaStateMachine> logger)
    {
        InstanceState(x => x.CurrentState);

        // Correlate by AlertId — explicit per overall.md §53.7.
        Event(() => AnomalyDetectedV1, c => c.CorrelateBy((s, ctx) => s.AlertId == ctx.Message.AlertId)
            .SelectId(ctx => ctx.Message.AlertId));

        Event(() => AnomalyDetectedV2, c => c.CorrelateBy((s, ctx) => s.AlertId == ctx.Message.AlertId)
            .SelectId(ctx => ctx.Message.AlertId));

        Event(() => TicketCreated, c => c.CorrelateById(ctx => ctx.Message.CorrelationId));
        Event(() => TicketCreationRejected, c => c.CorrelateById(ctx => ctx.Message.CorrelationId));
        Event(() => AlertLinked, c => c.CorrelateById(ctx => ctx.Message.CorrelationId));
        Event(() => AlertLinkRejected, c => c.CorrelateById(ctx => ctx.Message.CorrelationId));
        Event(() => ReconciliationRequested, c => c.CorrelateBy((s, ctx) => s.AlertId == ctx.Message.AlertId)
            .SelectId(ctx => ctx.Message.AlertId));

        Schedule(() => TicketCreationTimer, instance => instance.PendingTimeoutTokenId,
            s =>
            {
                s.Delay = TicketCreationTimeout;
                s.Received = r => r.CorrelateById(ctx => ctx.Message.CorrelationId);
            });
        Schedule(() => AlertLinkTimer, instance => instance.PendingTimeoutTokenId,
            s =>
            {
                s.Delay = AlertLinkTimeout;
                s.Received = r => r.CorrelateById(ctx => ctx.Message.CorrelationId);
            });

        // ===== Initial: nhận anomaly event → send CreateTicketFromAlertCommand =====
        //
        // FIX duplicate-saga: BatteryService publish CẢ V1 LẪN V2 cho cùng 1 alert
        // (AnomalyDetectionService — V2 là bản mở rộng thêm SiteId/Tier-2 fields).
        // Cả hai đều SelectId(AlertId) ⇒ CÙNG correlation id ⇒ 2 message tranh nhau tạo
        // saga instance ⇒ 23505 PK_alert_ticket_saga_states, và nhánh thua nhận AlertLinked
        // sai state ⇒ UnhandledEventException.
        //
        // Chỉ V2 được khởi tạo saga (nhiều field hơn). V1 KHÔNG còn ở Initially — nó vẫn
        // được DuringAny bắt để dedup, và NotificationService vẫn consume V1 như cũ.
        Initially(
            When(AnomalyDetectedV2)
                .Then(ctx => HydrateFromV2(ctx.Saga, ctx.Message))
                .Then(_ => AppMetrics.SagaStarted.Inc())
                .Then(_ => AppMetrics.SagaActive.Inc())
                .Activity(x => x.OfInstanceType<SendCreateTicketActivity>())
                .Schedule(TicketCreationTimer, ctx => new TicketCreationTimeoutFired(ctx.Saga.CorrelationId))
                .TransitionTo(TicketRequested),

            // Admin reconciliation — Ticket đã tồn tại, skip create, đi thẳng tới link Alert.
            When(ReconciliationRequested)
                .Then(ctx => HydrateFromReconciliation(ctx.Saga, ctx.Message))
                .Then(_ => AppMetrics.SagaStarted.Inc())
                .Then(_ => AppMetrics.SagaActive.Inc())
                .Activity(x => x.OfInstanceType<SendLinkAlertActivity>())
                .Schedule(AlertLinkTimer, ctx => new AlertLinkTimeoutFired(ctx.Saga.CorrelationId))
                .TransitionTo(AlertLinkRequested)
        );

        // ===== TicketRequested: chờ Ticket-side response =====
        During(TicketRequested,
            When(TicketCreated)
                .Unschedule(TicketCreationTimer)
                .Then(ctx =>
                {
                    ctx.Saga.TicketId = ctx.Message.TicketId;
                    ctx.Saga.TicketCode = ctx.Message.TicketCode;
                    ctx.Saga.TicketIsReused = ctx.Message.IsReused;
                })
                .TransitionTo(TicketProvisioned)
                .Activity(x => x.OfInstanceType<SendLinkAlertActivity>())
                .Schedule(AlertLinkTimer, ctx => new AlertLinkTimeoutFired(ctx.Saga.CorrelationId))
                .TransitionTo(AlertLinkRequested),

            When(TicketCreationRejected)
                .Unschedule(TicketCreationTimer)
                .Then(ctx => MarkFailed(ctx.Saga, "TicketRequested", ctx.Message.Reason, ctx.Message.ErrorCode))
                .Activity(x => x.OfInstanceType<PublishSagaFailedActivity>())
                .TransitionTo(Failed),

            When(TicketCreationTimer.Received)
                .Then(ctx =>
                {
                    AppMetrics.SagaTimeoutFired.WithLabels("ticket_requested").Inc();
                    ctx.Saga.RetryCount += 1;
                    logger.LogWarning("Saga {CorrelationId} TicketRequested timeout, retry={Retry}",
                        ctx.Saga.CorrelationId, ctx.Saga.RetryCount);
                })
                .IfElse(ctx => ctx.Saga.RetryCount < MaxRetryCount,
                    retry => retry
                        .Activity(x => x.OfInstanceType<SendCreateTicketActivity>())
                        .Schedule(TicketCreationTimer, ctx => new TicketCreationTimeoutFired(ctx.Saga.CorrelationId)),
                    giveUp => giveUp
                        .Then(ctx => MarkFailed(ctx.Saga, "TicketRequested", "Bounded retry exhausted (timeout)", "TIMEOUT_EXHAUSTED"))
                        .Activity(x => x.OfInstanceType<PublishSagaFailedActivity>())
                        .TransitionTo(Failed))
        );

        // ===== AlertLinkRequested: chờ Alert-side response =====
        During(AlertLinkRequested,
            When(AlertLinked)
                .Unschedule(AlertLinkTimer)
                .Then(ctx =>
                {
                    ctx.Saga.CompletedAt = DateTime.UtcNow;
                    var duration = (ctx.Saga.CompletedAt.Value - ctx.Saga.StartedAt).TotalSeconds;
                    AppMetrics.SagaCompleted.Inc();
                    AppMetrics.SagaDurationSeconds.Observe(duration);
                    AppMetrics.SagaActive.Dec();
                })
                .TransitionTo(Completed),

            When(AlertLinkRejected)
                .Unschedule(AlertLinkTimer)
                .Then(ctx => MarkFailed(ctx.Saga, "AlertLinkRequested", ctx.Message.Reason, ctx.Message.ErrorCode))
                .Activity(x => x.OfInstanceType<PublishSagaFailedActivity>())
                .TransitionTo(Failed),

            When(AlertLinkTimer.Received)
                .Then(ctx =>
                {
                    AppMetrics.SagaTimeoutFired.WithLabels("alert_link_requested").Inc();
                    ctx.Saga.RetryCount += 1;
                    logger.LogWarning("Saga {CorrelationId} AlertLinkRequested timeout, retry={Retry}",
                        ctx.Saga.CorrelationId, ctx.Saga.RetryCount);
                })
                .IfElse(ctx => ctx.Saga.RetryCount < MaxRetryCount,
                    retry => retry
                        .Activity(x => x.OfInstanceType<SendLinkAlertActivity>())
                        .Schedule(AlertLinkTimer, ctx => new AlertLinkTimeoutFired(ctx.Saga.CorrelationId)),
                    giveUp => giveUp
                        .Then(ctx => MarkFailed(ctx.Saga, "AlertLinkRequested", "Bounded retry exhausted (timeout)", "TIMEOUT_EXHAUSTED"))
                        .Activity(x => x.OfInstanceType<PublishSagaFailedActivity>())
                        .TransitionTo(Failed))
        );

        // ===== Completed / Failed: tombstone — duplicate event ignored =====
        DuringAny(
            When(AnomalyDetectedV1)
                .Then(_ => AppMetrics.SagaRedeliveryDeduped.Inc()),
            When(AnomalyDetectedV2)
                .Then(_ => AppMetrics.SagaRedeliveryDeduped.Inc())
        );

        SetCompletedWhenFinalized();
    }

    /// <summary>
    /// Hydrate saga từ event V1. HIỆN KHÔNG DÙNG — saga chỉ khởi tạo từ V2 (xem comment
    /// ở <c>Initially</c>: V1+V2 cùng correlation id gây tranh chấp tạo saga). Giữ lại để
    /// rollback nhanh nếu BatteryService ngừng publish V2.
    /// </summary>
    private static void HydrateFromV1(AlertTicketSagaState saga, BatteryAnomalyDetectedEvent evt)
    {
        saga.AlertId = evt.AlertId;
        saga.BatteryAssetId = evt.BatteryAssetId;
        saga.CustomerId = evt.CustomerId;
        saga.AssetSerialNumber = evt.AssetSerialNumber;
        saga.AnomalyType = evt.AnomalyType;
        saga.Severity = evt.Severity;
        saga.ThresholdValue = evt.ThresholdValue;
        saga.ActualValue = evt.ActualValue;
        saga.Unit = evt.Unit;
        saga.DetectedAt = evt.DetectedAt;
        saga.StartedAt = DateTime.UtcNow;
    }

    private static void HydrateFromV2(AlertTicketSagaState saga, BatteryAnomalyDetectedV2Event evt)
    {
        saga.AlertId = evt.AlertId;
        saga.BatteryAssetId = evt.BatteryAssetId;
        saga.CustomerId = evt.CustomerId;
        saga.SiteId = evt.SiteId;
        saga.AssetSerialNumber = evt.AssetSerialNumber;
        saga.AnomalyType = evt.AnomalyType;
        saga.Severity = evt.Severity;
        saga.ThresholdValue = evt.ThresholdValue;
        saga.ActualValue = evt.ActualValue;
        saga.Unit = evt.Unit;
        saga.DetectedAt = evt.DetectedAt;
        saga.InternalResistanceMilliohm = evt.InternalResistanceMilliohm;
        saga.CellVoltageDeltaMv = evt.CellVoltageDeltaMv;
        saga.EnvironmentalIncidentId = evt.EnvironmentalIncidentId;
        saga.AiPrescription = evt.AiPrescription;            // BE-AI (nullable — chỉ V2 từ SohPrediction job)

        // BE-AI structured — gói lại thành 1 JSON để mang qua chặng sau. Ticket từ threshold
        // engine không có tín hiệu AI nào ⇒ để null, không ghi chuỗi "{}" vô nghĩa vào DB.
        var aiPayload = new AiSuggestionPayload(
            ActionSteps: evt.AiActionSteps,
            PpeRequired: evt.AiPpeRequired,
            SopReferences: evt.AiSopReferences,
            EscalationConditions: evt.AiEscalationConditions,
            SafetyWarnings: evt.AiSafetyWarnings,
            KbDocRefs: evt.AiKbDocRefs,
            HumanVerificationRequired: evt.AiHumanVerificationRequired,
            Blocked: evt.AiBlocked,
            Enriched: evt.AiEnriched,
            LlmProvider: evt.AiLlmProvider,
            PrescriptionId: evt.AiPrescriptionId);
        saga.AiSuggestionJson = aiPayload.IsEmpty ? null : JsonSerializer.Serialize(aiPayload);

        saga.StartedAt = DateTime.UtcNow;
    }

    private static void HydrateFromReconciliation(AlertTicketSagaState saga, AlertTicketReconciliationCommand cmd)
    {
        saga.AlertId = cmd.AlertId;
        saga.TicketId = cmd.TicketId;
        saga.TicketCode = cmd.TicketCode;
        saga.BatteryAssetId = cmd.BatteryAssetId;
        saga.CustomerId = cmd.CustomerId;
        saga.AssetSerialNumber = cmd.AssetSerialNumber;
        saga.DetectedAt = cmd.DetectedAt;
        saga.TicketIsReused = true;
        saga.Unit = string.Empty;
        saga.StartedAt = DateTime.UtcNow;
    }

    private static void MarkFailed(AlertTicketSagaState saga, string stage, string reason, string? errorCode)
    {
        saga.FailedAtStage = stage;
        saga.FailureReason = reason;
        saga.FailureErrorCode = errorCode;
        saga.FailedAt = DateTime.UtcNow;
        AppMetrics.SagaFailed.WithLabels(stage).Inc();
        AppMetrics.SagaActive.Dec();
    }
}

/// <summary>Timeout fire event cho TicketRequested state.</summary>
public record TicketCreationTimeoutFired(Guid CorrelationId);

/// <summary>Timeout fire event cho AlertLinkRequested state.</summary>
public record AlertLinkTimeoutFired(Guid CorrelationId);
