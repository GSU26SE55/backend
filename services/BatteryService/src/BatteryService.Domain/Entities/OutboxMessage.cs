namespace BatteryService.Domain.Entities;

/// <summary>
/// Outbox pattern: event được ghi trong CÙNG transaction với entity gốc (Alert), sau đó
/// background relay đọc và publish lên RabbitMQ → đảm bảo exactly-once delivery.
/// Không kế thừa AuditableEntity vì là technical entity, không cần audit user.
/// </summary>
public class OutboxMessage
{
    public Guid Id { get; set; }

    /// <summary>Id của entity gốc phát sinh event (vd AlertId).</summary>
    public Guid AggregateId { get; set; }

    /// <summary>Tên type của integration event (vd "BatteryAnomalyDetectedEvent").</summary>
    public string Type { get; set; } = null!;

    /// <summary>Payload JSON serialized.</summary>
    public string Payload { get; set; } = null!;

    public DateTime OccurredAtUtc { get; set; }

    /// <summary>Null nếu chưa publish thành công.</summary>
    public DateTime? ProcessedAtUtc { get; set; }

    public int RetryCount { get; set; }

    public string? LastError { get; set; }
}
