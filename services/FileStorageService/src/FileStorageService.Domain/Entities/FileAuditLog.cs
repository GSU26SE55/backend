using SharedKernels.Domain;

namespace FileStorageService.Domain.Entities;

/// <summary>Audit log forensic của FileStorageService (Sprint audit #AUDIT-29). 14 cột chuẩn. Có local endpoint (Option C). Append-only.</summary>
public class FileAuditLog : AuditableEntity
{
    public Guid EventId { get; set; }
    public string ServiceName { get; set; } = "FileStorageService";
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
