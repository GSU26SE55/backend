using SharedContracts.Events.Root;

namespace SharedContracts.Interfaces;

public interface IMessageProducerService
{
    Task PublishAsync<T>(T @message, CancellationToken cancellationToken = default)
        where T : IntegrationEvent;
}
