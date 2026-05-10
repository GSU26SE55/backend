namespace SharedContracts.Events.Root;

public abstract record IntegrationEvent
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public DateTime OccurredAt { get; private set; } = DateTime.UtcNow;
}
