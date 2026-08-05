using SharedKernels.Domain;
using SharedKernels.Interfaces;
using SmsService.Domain.Enums;

namespace SmsService.Domain.Entities;

/// <summary>
/// Outbox cầu nối audit pipeline Hybrid cho SmsService (Sprint audit #AUDIT-35). Publish AuditCreatedEventV1 lên
/// aggregator. KHÔNG dùng chung OutboxMessage (schema khác). SmsAuditLog nội bộ giữ nguyên (source-of-truth chi tiết).
/// </summary>
public class SmsAuditOutbox : AuditableEntity, IAuditOutboxMessage
{
    public Guid EventId { get; set; }
    public string EventType { get; set; } = "AuditCreatedEventV1";
    public string Payload { get; set; } = string.Empty;
    public DateTime? ProcessedAt { get; set; }
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
    public AuditOutboxStatusEnum Status { get; set; } = AuditOutboxStatusEnum.Pending;
}
