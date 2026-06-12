using Prometheus;

namespace SharedInfrastructure.Metrics;

/// <summary>
/// Custom application metrics. Static class — định nghĩa 1 lần, dùng ở mọi service.
/// Wire vào code tại các điểm pattern quan trọng để track real-time trên Grafana.
/// </summary>
public static class AppMetrics
{
    // ===== Outbox Pattern (AuthService publisher) =====
    public static readonly Counter OutboxProcessed = Prometheus.Metrics.CreateCounter(
        "outbox_messages_processed_total",
        "Total outbox messages successfully published to RabbitMQ.",
        new CounterConfiguration { LabelNames = new[] { "event_type" } });

    public static readonly Counter OutboxFailures = Prometheus.Metrics.CreateCounter(
        "outbox_messages_failures_total",
        "Total failed publish attempts (RabbitMQ down, deserialize error, etc).",
        new CounterConfiguration { LabelNames = new[] { "reason" } });

    public static readonly Counter OutboxSkippedMaxRetry = Prometheus.Metrics.CreateCounter(
        "outbox_messages_skipped_total",
        "Total outbox messages skipped due to RetryCount >= MaxRetries (poison messages).");

    public static readonly Gauge OutboxPending = Prometheus.Metrics.CreateGauge(
        "outbox_messages_pending_count",
        "Current count of outbox messages waiting to be published.");

    // ===== Inbox Pattern (Email/Sms consumers) =====
    public static readonly Counter InboxProcessed = Prometheus.Metrics.CreateCounter(
        "inbox_messages_processed_total",
        "Total messages processed for the first time by consumer.",
        new CounterConfiguration { LabelNames = new[] { "consumer" } });

    public static readonly Counter InboxSkippedDuplicate = Prometheus.Metrics.CreateCounter(
        "inbox_messages_skipped_duplicate_total",
        "Total duplicate messages skipped by Inbox (MassTransit retry deduplicated).",
        new CounterConfiguration { LabelNames = new[] { "consumer" } });

    // ===== Idempotency-Key Middleware (AuthService) =====
    public static readonly Counter IdempotencyReplayHits = Prometheus.Metrics.CreateCounter(
        "idempotency_key_replay_hits_total",
        "Total times a cached response was replayed (client retry with same key).");

    public static readonly Counter IdempotencyConflicts = Prometheus.Metrics.CreateCounter(
        "idempotency_key_conflicts_total",
        "Total 409 Conflict responses (parallel duplicate request with same key).");

    public static readonly Counter IdempotencyReservations = Prometheus.Metrics.CreateCounter(
        "idempotency_key_reservations_total",
        "Total new key reservations (first-time request with key).");

    // ===== Alert–Ticket Saga (Sprint 5B #239 — overall.md §9.2 #4) =====
    public static readonly Counter SagaStarted = Prometheus.Metrics.CreateCounter(
        "saga_alert_ticket_started_total",
        "Total Alert–Ticket Saga instances started.");

    public static readonly Counter SagaCompleted = Prometheus.Metrics.CreateCounter(
        "saga_alert_ticket_completed_total",
        "Total Saga instances completed (Ticket created + Alert linked).");

    public static readonly Counter SagaFailed = Prometheus.Metrics.CreateCounter(
        "saga_alert_ticket_failed_total",
        "Total Saga instances entered Failed state (terminal).",
        new CounterConfiguration { LabelNames = new[] { "reason" } });

    public static readonly Counter SagaReprocessed = Prometheus.Metrics.CreateCounter(
        "saga_alert_ticket_reprocessed_total",
        "Total admin reprocess attempts on Failed sagas.");

    public static readonly Histogram SagaDurationSeconds = Prometheus.Metrics.CreateHistogram(
        "saga_alert_ticket_duration_seconds",
        "Saga end-to-end duration from Initial → Completed.",
        new HistogramConfiguration
        {
            Buckets = new double[] { 0.5, 1, 2, 4, 8, 16, 32, 64 }
        });

    public static readonly Gauge SagaActive = Prometheus.Metrics.CreateGauge(
        "saga_alert_ticket_active_count",
        "Current count of in-flight sagas (not terminal).");

    public static readonly Counter SagaTimeoutFired = Prometheus.Metrics.CreateCounter(
        "saga_alert_ticket_timeout_fired_total",
        "Total Quartz timeouts fired on stuck sagas.",
        new CounterConfiguration { LabelNames = new[] { "stage" } });

    public static readonly Counter SagaRedeliveryDeduped = Prometheus.Metrics.CreateCounter(
        "saga_alert_ticket_redelivery_deduped_total",
        "Total duplicate event redeliveries deduped via OriginAlertId uniqueness.");
}
