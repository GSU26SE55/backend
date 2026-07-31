namespace TicketService.Domain.Entities;

/// <summary>
/// Outbox pattern: event được ghi trong CÙNG transaction với ticket action, sau đó
/// background relay đọc và publish lên RabbitMQ. Delivery là at-least-once; consumer
/// phải dùng Inbox/idempotency để chịu được message bị gửi lặp.
/// Technical outbox rows do not need user audit fields (non-auditable).
/// </summary>
public class OutboxMessage
{
    public Guid Id { get; set; }

    /// <summary>Id của entity gốc phát sinh event (vd TicketId).</summary>
    public Guid AggregateId { get; set; }

    /// <summary>Tên type của integration event (vd "TicketCreatedEvent").</summary>
    public string Type { get; set; } = null!;

    /// <summary>Payload JSON serialized.</summary>
    public string Payload { get; set; } = null!;

    public DateTime OccurredAtUtc { get; set; }

    /// <summary>Null nếu chưa publish thành công.</summary>
    public DateTime? ProcessedAtUtc { get; set; }

    public int RetryCount { get; set; }

    public string? LastError { get; set; }

    public string? LeaseOwner { get; set; }

    public DateTime? LeaseUntilUtc { get; set; }
}
