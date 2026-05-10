namespace SharedInfrastructure.Idempotency;

public interface IInboxStore
{
    Task<bool> TryMarkProcessedAsync(Guid messageId, string consumerName, CancellationToken cancellationToken = default);
}
