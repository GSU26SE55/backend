using SharedKernels.Domain;

namespace TicketService.Domain.Entities;

public class OutboxMessage : AuditableEntity
{
    public string EventType { get; set; }
    public string Payload { get; set; }
    public DateTime OccurredAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
    public string? CorrelationId { get; set; }
}
