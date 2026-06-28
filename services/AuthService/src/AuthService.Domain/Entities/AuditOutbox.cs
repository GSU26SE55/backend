using AuthService.Domain.Enums;
using SharedKernels.Domain;

namespace AuthService.Domain.Entities;

/// <summary>
/// Outbox RIÊNG cho audit pipeline (Sprint audit #AUDIT-07) — KHÔNG dùng chung <see cref="OutboxMessage"/> (AUTH-15)
/// vì schema khác (event_id unique idempotency + status enum + payload jsonb).
///
/// <para>Ghi CÙNG transaction với <see cref="AuditLog"/> (#AUDIT-09) → <c>AuditOutboxRelayBackgroundService</c>
/// (#AUDIT-08) poll publish <c>AuditCreatedEventV1</c> lên exchange <c>audit.events</c>.</para>
/// </summary>
public class AuditOutbox : AuditableEntity
{
    /// <summary>Idempotency key — = AuditLog.EventId. Unique.</summary>
    public Guid EventId { get; set; }

    public string EventType { get; set; } = "AuditCreatedEventV1";

    /// <summary>Payload JSON của AuditCreatedEventV1 (cột jsonb).</summary>
    public string Payload { get; set; } = string.Empty;

    public DateTime? ProcessedAt { get; set; }

    public int RetryCount { get; set; }

    public string? LastError { get; set; }

    public AuditOutboxStatusEnum Status { get; set; } = AuditOutboxStatusEnum.Pending;
}
