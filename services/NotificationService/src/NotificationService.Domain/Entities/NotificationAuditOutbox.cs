using NotificationService.Domain.Enums;
using SharedKernels.Domain;

namespace NotificationService.Domain.Entities;

/// <summary>Outbox riêng cho audit pipeline NotificationService (Sprint audit #AUDIT-34). Pattern giống AuthService #AUDIT-07.</summary>
public class NotificationAuditOutbox : AuditableEntity
{
    public Guid EventId { get; set; }
    public string EventType { get; set; } = "AuditCreatedEventV1";
    public string Payload { get; set; } = string.Empty;
    public DateTime? ProcessedAt { get; set; }
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
    public AuditOutboxStatusEnum Status { get; set; } = AuditOutboxStatusEnum.Pending;
}
