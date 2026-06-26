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

    // ============== #AUTH-78: Auth-domain metrics ==============

    /// <summary>Tổng số attempt login (result = "success" | "wrong_password" | "account_locked" | ...)</summary>
    public static readonly Counter AuthLoginTotal = Prometheus.Metrics.CreateCounter(
        "auth_login_total",
        "Total login attempts by outcome.",
        new CounterConfiguration { LabelNames = new[] { "result" } });

    /// <summary>Tổng số 2FA challenge (result = "totp_success" | "backup_success" | "sms_success" | "wrong_code" | "expired" | ...)</summary>
    public static readonly Counter Auth2FAChallengeTotal = Prometheus.Metrics.CreateCounter(
        "auth_2fa_challenge_total",
        "Total 2FA challenge attempts by outcome.",
        new CounterConfiguration { LabelNames = new[] { "result" } });

    /// <summary>Tổng số OTP đã generate/verify (purpose = "register" | "password_reset" | "email_change" | "phone_verify" | "2fa_sms"; result = "generated" | "verified" | "expired" | "wrong")</summary>
    public static readonly Counter AuthOtpUsageTotal = Prometheus.Metrics.CreateCounter(
        "auth_otp_usage_total",
        "Total OTP generation + verification events.",
        new CounterConfiguration { LabelNames = new[] { "purpose", "result" } });

    /// <summary>Tổng số refresh token rotation (result = "success" | "reuse_detected" | "expired" | "device_mismatch")</summary>
    public static readonly Counter AuthRefreshTokenTotal = Prometheus.Metrics.CreateCounter(
        "auth_refresh_token_total",
        "Total refresh token rotation outcomes.",
        new CounterConfiguration { LabelNames = new[] { "result" } });

    // ============== Sprint audit #AUDIT-44: Hybrid Audit pipeline metrics ==============

    /// <summary>Tổng audit event đã ingest vào audit_aggregate (label service/action/severity).</summary>
    public static readonly Counter AuditEventsTotal = Prometheus.Metrics.CreateCounter(
        "audit_events_total",
        "Total audit events ingested into audit_aggregate read-store.",
        new CounterConfiguration { LabelNames = new[] { "service", "action", "severity" } });

    /// <summary>Lag (giây) từ lúc action xảy ra (occurred_at) đến lúc aggregator consume. SLO p99 &lt; 10s.</summary>
    public static readonly Histogram AuditConsumerLagSeconds = Prometheus.Metrics.CreateHistogram(
        "audit_consumer_lag_seconds",
        "Lag from action occurred_at to aggregator ingest (seconds).",
        new HistogramConfiguration { Buckets = Histogram.ExponentialBuckets(0.1, 2, 12) });

    /// <summary>Số entry audit_outbox đang Pending mỗi service (relay set mỗi tick). Alert nếu &gt; 1000.</summary>
    public static readonly Gauge AuditOutboxPending = Prometheus.Metrics.CreateGauge(
        "audit_outbox_pending_total",
        "Current count of pending audit_outbox entries per service.",
        new GaugeConfiguration { LabelNames = new[] { "service" } });

    /// <summary>Số message trong audit DLQ (aggregator.audit.events.dlq). Alert nếu &gt; 100.</summary>
    public static readonly Gauge AuditDlqSize = Prometheus.Metrics.CreateGauge(
        "audit_dlq_size_total",
        "Current size of the audit dead-letter queue.");
}
