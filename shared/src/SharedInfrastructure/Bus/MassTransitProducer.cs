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
        // Publish theo RUNTIME type, không phải compile-time T. OutboxRelay deserialize event
        // thành kiểu tĩnh IntegrationEvent rồi gọi PublishAsync(evt) → T=IntegrationEvent →
        // MassTransit route lên exchange "IntegrationEvent" (sai) thay vì exchange của event
        // cụ thể (BatteryAnomalyDetectedV2Event) mà saga bind vào → saga không bao giờ nhận.
        // Publish(object, Type) ép MassTransit dùng đúng type cụ thể để route.
        return _publishEndpoint.Publish(@message!, @message!.GetType(), cancellationToken);
    }
}
