using BatteryService.Domain.Enums;
using SharedKernels.Domain;
using SharedKernels.Interfaces;

namespace BatteryService.Domain.Entities;

/// <summary>Outbox riêng cho audit pipeline BatteryService (Sprint audit #AUDIT-21). Pattern giống AuthService #AUDIT-07.</summary>
public class BatteryAuditOutbox : AuditableEntity, IAuditOutboxMessage
{
    public Guid EventId { get; set; }
    public string EventType { get; set; } = "AuditCreatedEventV1";
    public string Payload { get; set; } = string.Empty;
    public DateTime? ProcessedAt { get; set; }
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
    public AuditOutboxStatusEnum Status { get; set; } = AuditOutboxStatusEnum.Pending;
}
