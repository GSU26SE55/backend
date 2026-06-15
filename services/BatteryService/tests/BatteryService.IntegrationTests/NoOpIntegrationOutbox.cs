using SharedContracts.Events.Root;
using SharedContracts.Interfaces;

namespace BatteryService.IntegrationTests;

internal sealed class NoOpIntegrationOutbox : IIntegrationEventOutboxWriter
{
    public static readonly NoOpIntegrationOutbox Instance = new();
    public Task WriteAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IntegrationEvent
        => Task.CompletedTask;
}
