using MassTransit;
using SharedContracts.Events.Root;
using SharedContracts.Interfaces;

namespace SharedInfrastructure.Bus;

public class MassTransitProducer : IMessageProducerService, IIntegrationEventTransport
{
    private readonly IPublishEndpoint _publishEndpoint;
    public MassTransitProducer(IPublishEndpoint publishEndpoint)
    {
        _publishEndpoint = publishEndpoint;
    }
    public Task PublishAsync<T>(T @message, CancellationToken cancellationToken = default) where T : IntegrationEvent
    {
        return _publishEndpoint.Publish(@message, cancellationToken);
    }
}
