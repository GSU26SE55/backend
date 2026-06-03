namespace SharedContracts.Events;

public record SlaBreachedEvent
{
    public Guid TicketId { get; init; }
    public DateTime BreachedAt { get; init; }
}

public record SlaWarningEvent
{
    public Guid TicketId { get; init; }
    public DateTime WarningAt { get; init; }
    public double Percentage { get; init; }
}
