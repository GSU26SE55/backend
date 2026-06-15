using SharedContracts.Events.Root;
using SharedContracts.Interfaces;

namespace BatteryService.UnitTests.Helpers;

internal sealed class NoOpOutbox : IIntegrationEventOutboxWriter
{
    public static readonly NoOpOutbox Instance = new();
    public Task WriteAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IntegrationEvent
        => Task.CompletedTask;
}
