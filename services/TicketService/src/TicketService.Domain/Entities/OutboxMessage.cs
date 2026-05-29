using SharedKernels.Domain;

namespace TicketService.Domain.Entities;

public class OutboxMessage : AuditableEntity
{
    public required string EventType { get; set; }
    public required string Payload { get; set; }
    public DateTime OccurredAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
    public string? CorrelationId { get; set; }
}
