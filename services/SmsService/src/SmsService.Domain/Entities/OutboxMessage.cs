using SharedKernels.Domain;

namespace SmsService.Domain.Entities;

/// <summary>
/// Bản ghi Outbox Pattern — INSERT cùng transaction với business data trong handler,
/// <c>OutboxRelayBackgroundService</c> poll và publish ra RabbitMQ sau.
/// </summary>
public class OutboxMessage : AuditableEntity
{
    public string EventType { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
}
