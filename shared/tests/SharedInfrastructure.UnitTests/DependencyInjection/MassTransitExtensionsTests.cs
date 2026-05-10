using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedContracts.Events.Root;
using SharedContracts.Interfaces;
using SharedInfrastructure.Bus;

namespace SharedInfrastructure.UnitTests.DependencyInjection;

/// <summary>
/// Test AddMessageBus: registers IMessageProducerService + MassTransit infrastructure (IBus, IPublishEndpoint).
/// Không boot RabbitMQ thật — chỉ verify DI graph.
/// </summary>
public class MassTransitExtensionsTests
{
    public class FakeConsumer : IConsumer<FakeEvent>
    {
        public Task Consume(ConsumeContext<FakeEvent> context) => Task.CompletedTask;
    }

    public record FakeEvent(string Payload) : IntegrationEvent;

    private static IConfiguration BuildConfig() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RabbitMQ:Host"] = "localhost",
            ["RabbitMQ:Username"] = "guest",
            ["RabbitMQ:Password"] = "guest"
        }).Build();

    [Fact]
    public void AddMessageBus_RegistersMessageProducer_AsScoped()
    {
        var services = new ServiceCollection();

        services.AddMessageBus(BuildConfig());

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IMessageProducerService));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Scoped);
        descriptor.ImplementationType.Should().Be(typeof(MassTransitProducer));
    }

    [Fact]
    public void AddMessageBus_RegistersMassTransitCoreServices()
    {
        var services = new ServiceCollection();

        services.AddMessageBus(BuildConfig());

        // MassTransit infrastructure: IBus, IBusControl, IPublishEndpoint, ISendEndpointProvider.
        services.Should().Contain(d => d.ServiceType == typeof(IBus));
        services.Should().Contain(d => d.ServiceType == typeof(IBusControl));
        services.Should().Contain(d => d.ServiceType == typeof(IPublishEndpoint));
        services.Should().Contain(d => d.ServiceType == typeof(ISendEndpointProvider));
    }

    [Fact]
    public void AddMessageBus_NoConsumerAssemblies_StillRegistersBus()
    {
        var services = new ServiceCollection();

        services.AddMessageBus(BuildConfig());

        services.Should().Contain(d => d.ServiceType == typeof(IBus));
    }

    [Fact]
    public void AddMessageBus_WithConsumerAssemblies_RegistersConsumers()
    {
        var services = new ServiceCollection();

        services.AddMessageBus(BuildConfig(), typeof(FakeConsumer).Assembly);

        // Consumer được register có thể resolve từ DI.
        var consumer = services.FirstOrDefault(d => d.ServiceType == typeof(FakeConsumer));
        consumer.Should().NotBeNull("AddConsumers(assembly) phải register FakeConsumer");
    }

    [Fact]
    public void AddMessageBus_ReturnsServiceCollection_ForChaining()
    {
        var services = new ServiceCollection();

        var returned = services.AddMessageBus(BuildConfig());

        returned.Should().BeSameAs(services);
    }
}
