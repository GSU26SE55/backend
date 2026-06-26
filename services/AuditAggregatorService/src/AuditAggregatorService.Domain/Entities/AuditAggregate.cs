using SharedKernels.Domain;

namespace AuditAggregatorService.Domain.Entities;

/// <summary>
/// Read-store tổng hợp audit cross-service (Sprint audit <c>#AUDIT-14</c>).
///
/// <para>Là <b>materialized view</b> (read-only, rebuild được từ source-of-truth ở từng service) — KHÔNG phải
/// nguồn chân lý. Map vào bảng <c>audit_aggregate</c> partitioned by month theo <see cref="OccurredAt"/>
/// (pg_partman, <c>#AUDIT-14</c>). Idempotent INSERT theo <see cref="EventId"/> (<c>#AUDIT-15</c>).</para>
///
/// <para>Extend <see cref="AuditableEntity"/> để nhất quán với các bảng audit khác (AuthService AuditLog,
/// BatteryAuditLog, TicketAuditLog...): <c>Id</c> + <c>CreatedAt</c> (= thời điểm ingest vào read-store) + audit
/// columns. <c>CreatedBy</c>/<c>UpdatedAt</c> hầu như null (consumer-driven, không user). Bất biến: không soft-delete
/// (redaction <c>#AUDIT-42</c> chỉ UPDATE PII → <c>[REDACTED]</c>; retention <c>#AUDIT-41</c> hard-delete theo partition).</para>
/// </summary>
public class AuditAggregate : AuditableEntity
{
    // ---- Idempotency + provenance ----
    /// <summary>Idempotency key — Guid v7 từ producer. Unique cùng <see cref="OccurredAt"/> trong partitioned table.</summary>
    public Guid EventId { get; set; }

    /// <summary>Service phát sinh audit, vd "AuthService".</summary>
    public string ServiceName { get; set; } = string.Empty;

    // ---- What ----
    public string ActionCode { get; set; } = string.Empty;
    public string ActionCategory { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;

    // ---- Target ----
    public string? TargetType { get; set; }
    public Guid? TargetId { get; set; }
    public string? TargetDisplay { get; set; }

    // ---- Actor ----
    public Guid? ActorAccountId { get; set; }
    public string? ActorRole { get; set; }
    public string? ActorDisplay { get; set; }
    public string? ActorIp { get; set; }
    public string? ActorUserAgent { get; set; }

    // ---- Result ----
    public bool IsSuccess { get; set; }
    public string? ErrorCode { get; set; }
    public string? Reason { get; set; }

    /// <summary>Payload bổ sung dạng JSON (cột jsonb + GIN index).</summary>
    public string? MetadataJson { get; set; }

    // ---- Correlation ----
    public Guid? CorrelationId { get; set; }
    public Guid? CausationId { get; set; }

    // ---- Time ----
    /// <summary>UTC — thời điểm hành động xảy ra. <b>Partition key</b> (PARTITION BY RANGE).</summary>
    public DateTime OccurredAt { get; set; }

    /// <summary>UTC — thời điểm ghi vào source DB (lấy từ event).</summary>
    public DateTime RecordedAt { get; set; }

    // Thời điểm aggregator INSERT vào read-store = AuditableEntity.CreatedAt (interceptor set; FromEvent set fallback).

    // ---- Geo enrichment (#AUDIT-16, nullable — optional) ----
    public string? GeoCountry { get; set; }
    public string? GeoCity { get; set; }

    /// <summary>Map từ integration event sang entity read-store.</summary>
    public static AuditAggregate FromEvent(
        Guid eventId, string serviceName, string actionCode, string actionCategory, string severity,
        string? targetType, Guid? targetId, string? targetDisplay,
        Guid? actorAccountId, string? actorRole, string? actorDisplay, string? actorIp, string? actorUserAgent,
        bool isSuccess, string? errorCode, string? reason, string? metadataJson,
        Guid? correlationId, Guid? causationId, DateTime occurredAt, DateTime recordedAt)
        => new()
        {
            EventId = eventId,
            ServiceName = serviceName,
            ActionCode = actionCode,
            ActionCategory = actionCategory,
            Severity = severity,
            TargetType = targetType,
            TargetId = targetId,
            TargetDisplay = targetDisplay,
            ActorAccountId = actorAccountId,
            ActorRole = actorRole,
            ActorDisplay = actorDisplay,
            ActorIp = actorIp,
            ActorUserAgent = actorUserAgent,
            IsSuccess = isSuccess,
            ErrorCode = errorCode,
            Reason = reason,
            MetadataJson = metadataJson,
            CorrelationId = correlationId,
            CausationId = causationId,
            OccurredAt = occurredAt,
            RecordedAt = recordedAt,
            CreatedAt = recordedAt,   // fallback nếu interceptor không active (vd test); prod interceptor set UtcNow lúc ingest.
        };
}
