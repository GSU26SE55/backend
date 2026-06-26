namespace SharedContracts.Events.Audit;

/// <summary>
/// Integration event chuẩn của Hybrid Audit Architecture (Sprint audit — <c>#AUDIT-01</c>).
///
/// <para>
/// Mỗi service publish event này (qua Outbox + <c>*AuditOutboxRelayBackgroundService</c>) khi có một hành động
/// cần ghi audit forensic. <c>AuditAggregatorService</c> consume → INSERT vào read-store <c>audit_aggregate</c>
/// (idempotent theo <see cref="EventId"/>).
/// </para>
///
/// <para>
/// <b>Lưu ý thiết kế</b> — KHÔNG kế thừa <c>IntegrationEvent</c> (vốn cấp <c>Id</c> = Guid v4 + <c>OccurredAt</c> auto):
/// audit cần idempotency key riêng <see cref="EventId"/> = <b>Guid v7</b> (time-sortable) do producer set, và 2 mốc
/// thời gian tách biệt <see cref="OccurredAt"/> (lúc hành động xảy ra) vs <see cref="RecordedAt"/> (lúc ghi DB) để
/// phát hiện clock skew. Versioned tên <c>V1</c> — thay đổi schema breaking phải bump <c>V2</c> (Phụ lục B §B.5).
/// </para>
///
/// Routing key khi publish: <c>audit.{service}.{category}.{severity}</c> (exchange <c>audit.events</c>, <c>#AUDIT-05</c>).
/// </summary>
/// <param name="EventId">Idempotency key — Guid v7 (time-sortable). Producer set bằng helper (net8: polyfill v7).</param>
/// <param name="ServiceName">Service phát sinh audit, vd <c>"AuthService"</c>, <c>"BatteryService"</c>.</param>
/// <param name="ActionCode">Mã hành động chuẩn — xem <see cref="SharedContracts.Audit.ActionCodes"/>.</param>
/// <param name="ActionCategory">Nhóm hành động — xem <see cref="SharedContracts.Audit.AuditCategories"/> (9 category cố định).</param>
/// <param name="Severity">Mức độ — xem <see cref="SharedContracts.Audit.Severities"/> (Info/Warning/Critical/Security).</param>
/// <param name="TargetType">Loại đối tượng bị tác động — xem <see cref="SharedContracts.Audit.TargetTypes"/>.</param>
/// <param name="TargetId">Id đối tượng bị tác động (nullable — vd login fail chưa có target cụ thể).</param>
/// <param name="TargetDisplay">Tên hiển thị của target (denormalized cho UI/forensic), vd email/serial.</param>
/// <param name="ActorAccountId">Account thực hiện hành động (null nếu anonymous/system).</param>
/// <param name="ActorRole">Role của actor lúc hành động: Admin/Manager/Staff/Customer/System.</param>
/// <param name="ActorDisplay">Tên hiển thị actor (denormalized), vd full name/email.</param>
/// <param name="ActorIp">IP của actor (cho geo enrichment + security investigation), null nếu không có HttpContext.</param>
/// <param name="ActorUserAgent">User-Agent header của actor, null nếu không có.</param>
/// <param name="IsSuccess">Hành động thành công hay thất bại (vd login fail vẫn audit với IsSuccess=false).</param>
/// <param name="ErrorCode">Mã lỗi khi <see cref="IsSuccess"/>=false (null khi success).</param>
/// <param name="Reason">Lý do/ghi chú (vd priority override reason). Đã sanitize PII trước khi set.</param>
/// <param name="MetadataJson">Payload bổ sung dạng JSON (flexible, lưu vào cột jsonb + GIN index ở aggregate).</param>
/// <param name="CorrelationId">Correlation id xuyên suốt request (từ <c>CorrelationIdMiddleware</c> AUTH-77).</param>
/// <param name="CausationId">Event id của parent event đã gây ra hành động này (cross-service causation chain).</param>
/// <param name="OccurredAt">UTC — thời điểm hành động thực sự xảy ra (handler set).</param>
/// <param name="RecordedAt">UTC — thời điểm ghi vào source-of-truth DB (DB default now() hoặc handler set).</param>
public record AuditCreatedEventV1(
    Guid EventId,
    string ServiceName,
    string ActionCode,
    string ActionCategory,
    string Severity,
    string? TargetType,
    Guid? TargetId,
    string? TargetDisplay,
    Guid? ActorAccountId,
    string? ActorRole,
    string? ActorDisplay,
    string? ActorIp,
    string? ActorUserAgent,
    bool IsSuccess,
    string? ErrorCode,
    string? Reason,
    string? MetadataJson,
    Guid? CorrelationId,
    Guid? CausationId,
    DateTime OccurredAt,
    DateTime RecordedAt
);
