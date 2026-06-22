using SharedContracts.Events.Root;

namespace SharedContracts.Events;

public record SlaBreachedEvent : IntegrationEvent
{
    public Guid TicketId { get; init; }
    public DateTime BreachedAt { get; init; }
    public string Priority { get; init; } = string.Empty;
}

public record SlaWarningEvent : IntegrationEvent
{
    public Guid TicketId { get; init; }
    public DateTime WarningAt { get; init; }
    public double Percentage { get; init; }
}
