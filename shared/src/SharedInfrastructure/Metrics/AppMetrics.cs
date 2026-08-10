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

    // ===== Chat Hub Metrics (#572) =====

    /// <summary>Total chat events (add/edit/delete/react/pin) per ticket. Labels: ticket_id, event_type.</summary>
    public static readonly Counter ChatEventsTotal = Prometheus.Metrics.CreateCounter(
        "chat_events_total",
        "Total chat events processed.",
        new CounterConfiguration { LabelNames = new[] { "ticket_id", "event_type" } });

    /// <summary>Total @mention events by role. Labels: role (Staff/Manager/Admin/Customer).</summary>
    public static readonly Counter ChatMentionCountTotal = Prometheus.Metrics.CreateCounter(
        "chat_mention_count_total",
        "Total mention events.",
        new CounterConfiguration { LabelNames = new[] { "role" } });

    /// <summary>Total reaction add/remove events by emoji. Labels: reaction_type.</summary>
    public static readonly Counter ChatReactionCountTotal = Prometheus.Metrics.CreateCounter(
        "chat_reaction_count_total",
        "Total reaction events.",
        new CounterConfiguration { LabelNames = new[] { "reaction_type" } });

    /// <summary>Current connected SignalR users per ticket. Labels: ticket_id.</summary>
    public static readonly Gauge SignalRConnectedUsersTotal = Prometheus.Metrics.CreateGauge(
        "signalr_connected_users_total",
        "Current connected SignalR users per ticket.",
        new GaugeConfiguration { LabelNames = new[] { "ticket_id" } });

    /// <summary>Pending outbox messages for chat events.</summary>
    public static readonly Gauge ChatOutboxPendingTotal = Prometheus.Metrics.CreateGauge(
        "chat_outbox_pending_total",
        "Current count of chat-related outbox messages pending publish.");

    /// <summary>Latency histogram for AI KB suggestion calls.</summary>
    public static readonly Histogram ChatAiSuggestLatencySeconds = Prometheus.Metrics.CreateHistogram(
        "chat_ai_suggest_latency_seconds",
        "Latency for AI/KB suggestion endpoint calls.",
        new HistogramConfiguration
        {
            Buckets = new double[] { 0.05, 0.1, 0.25, 0.5, 1.0, 2.0, 5.0 }
        });

    // ===== NotificationService delivery (Sprint 6.3 NOTI3-07 / #707) =====
    // Trước sprint này toàn NotificationService chỉ có đúng 1 metric (AuditOutboxPending):
    // Sprint 6.2 vừa bật tầng gửi mà không có cách nào biết nó đang hỏng.
    // "Delivery rate tách theo channel" là metric cơ bản nhất của một notification service —
    // mỗi kênh có kiểu hỏng khác nhau nên KHÔNG được gộp chung.

    /// <summary>Notification đã giao thành công xuống channel. Labels: channel, type.</summary>
    public static readonly Counter NotificationSentTotal = Prometheus.Metrics.CreateCounter(
        "notification_sent_total",
        "Total notifications successfully delivered to their channel.",
        new CounterConfiguration { LabelNames = new[] { "channel", "type" } });

    /// <summary>
    /// Notification thất bại VĨNH VIỄN (hết lượt thử hoặc lỗi không thể phục hồi).
    /// Labels: channel, reason — <c>reason</c> phải là nhóm ngắn (vd "channel_disabled",
    /// "no_device_token", "no_email", "provider_error"), KHÔNG nhét message thô vào label
    /// để tránh nổ cardinality của Prometheus.
    /// </summary>
    public static readonly Counter NotificationFailedTotal = Prometheus.Metrics.CreateCounter(
        "notification_failed_total",
        "Total notifications that permanently failed delivery.",
        new CounterConfiguration { LabelNames = new[] { "channel", "reason" } });

    /// <summary>Lần gửi lỗi tạm thời, sẽ retry. Labels: channel.</summary>
    public static readonly Counter NotificationRetryTotal = Prometheus.Metrics.CreateCounter(
        "notification_retry_total",
        "Total transient delivery failures scheduled for retry.",
        new CounterConfiguration { LabelNames = new[] { "channel" } });

    /// <summary>Notification bị hoãn có chủ đích. Labels: channel, reason (quiet_hours | digest).</summary>
    public static readonly Counter NotificationDeferredTotal = Prometheus.Metrics.CreateCounter(
        "notification_deferred_total",
        "Total notifications intentionally deferred (quiet hours, digest window).",
        new CounterConfiguration { LabelNames = new[] { "channel", "reason" } });

    /// <summary>
    /// Độ trễ end-to-end: từ lúc consumer ghi record tới lúc giao xong xuống channel.
    /// Bucket kéo dài tới 1 giờ vì record có thể bị hoãn qua quiet hours / digest.
    /// </summary>
    public static readonly Histogram NotificationDeliveryLatencySeconds = Prometheus.Metrics.CreateHistogram(
        "notification_delivery_latency_seconds",
        "Latency from notification record creation to successful channel delivery.",
        new HistogramConfiguration
        {
            LabelNames = new[] { "channel" },
            Buckets = new double[] { 1, 5, 15, 30, 60, 300, 900, 3600 }
        });

    /// <summary>Số record đang chờ giao (Status=Pending, tới hạn). Tín hiệu queue lag.</summary>
    public static readonly Gauge NotificationPendingTotal = Prometheus.Metrics.CreateGauge(
        "notification_pending_total",
        "Current count of notification records awaiting dispatch.");

    /// <summary>
    /// Số message trong dead-letter queue của NotificationService.
    /// Trạng thái khoẻ mạnh là 0 — có message xuất hiện là phải điều tra.
    /// </summary>
    public static readonly Gauge NotificationDlqSize = Prometheus.Metrics.CreateGauge(
        "notification_dlq_size",
        "Current message count in NotificationService dead-letter (_error) queues.",
        new GaugeConfiguration { LabelNames = new[] { "queue" } });

    /// <summary>
    /// Request HTTP bị chặn bởi hạn mức nền (<c>StandardRateLimitOptions</c>).
    /// Labels: scope (authenticated|anonymous) — biết bậc nào đang chạm trần mới chỉnh đúng con số.
    /// </summary>
    public static readonly Counter HttpRateLimitedTotal = Prometheus.Metrics.CreateCounter(
        "http_rate_limited_total",
        "Total HTTP requests rejected with 429 by the standard rate limiter.",
        new CounterConfiguration { LabelNames = new[] { "scope" } });

    /// <summary>Notification bị chặn bởi rate limit per-user (NOTI3-06). Labels: type.</summary>
    public static readonly Counter NotificationRateLimitedTotal = Prometheus.Metrics.CreateCounter(
        "notification_rate_limited_total",
        "Total notifications throttled by the per-user rate limit.",
        // reason: per_hour | per_type — biết trần nào bị chạm mới chỉnh đúng tham số.
        new CounterConfiguration { LabelNames = new[] { "channel", "reason" } });

    /// <summary>Bản SMS bù sinh ra do push không có receipt (NOTI3-05). Labels: from_channel.</summary>
    public static readonly Counter NotificationFallbackTotal = Prometheus.Metrics.CreateCounter(
        "notification_fallback_total",
        "Total fallback notifications generated when the primary channel had no delivery receipt.",
        new CounterConfiguration { LabelNames = new[] { "from_channel", "to_channel" } });

    // ===== Expo push receipt (Sprint 6.3 NOTI3-02 / #702) =====

    /// <summary>Receipt Expo đã đối soát. Labels: status (ok|error), error_code.</summary>
    public static readonly Counter ExpoReceiptTotal = Prometheus.Metrics.CreateCounter(
        "expo_push_receipt_total",
        "Total Expo push receipts reconciled.",
        new CounterConfiguration { LabelNames = new[] { "status", "error_code" } });

    /// <summary>Device token bị vô hiệu hoá do Expo báo DeviceNotRegistered.</summary>
    public static readonly Counter ExpoTokenDeactivatedTotal = Prometheus.Metrics.CreateCounter(
        "expo_push_token_deactivated_total",
        "Total device tokens deactivated after Expo reported them unusable.");
}
