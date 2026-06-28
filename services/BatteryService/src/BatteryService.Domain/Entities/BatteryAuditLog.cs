using SharedKernels.Domain;

namespace BatteryService.Domain.Entities;

/// <summary>
/// Audit log forensic của BatteryService (Sprint audit #AUDIT-20). 14 cột chuẩn Hybrid Audit.
/// Bao gồm cả Alert audit (#AUDIT-31 — Alert host trong BatteryService, D14) — phân biệt qua <see cref="ActionCategory"/>/<see cref="ActionCode"/>.
/// Append-only (trigger soft mode). Source-of-truth; publish AuditCreatedEventV1 qua audit_outbox.
/// </summary>
public class BatteryAuditLog : AuditableEntity
{
    public Guid EventId { get; set; }
    public string ServiceName { get; set; } = "BatteryService";
    public string ActionCode { get; set; } = string.Empty;
    public string ActionCategory { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string? TargetType { get; set; }
    public Guid? TargetId { get; set; }
    public string? TargetDisplay { get; set; }
    public Guid? ActorAccountId { get; set; }
    public string? ActorRole { get; set; }
    public string? ActorDisplay { get; set; }
    public string? ActorIp { get; set; }
    public string? ActorUserAgent { get; set; }
    public bool IsSuccess { get; set; } = true;
    public string? ErrorCode { get; set; }
    public string? Reason { get; set; }
    public string? MetadataJson { get; set; }
    public Guid? CorrelationId { get; set; }
    public Guid? CausationId { get; set; }
    public DateTime OccurredAt { get; set; }
    public DateTime RecordedAt { get; set; }
}
