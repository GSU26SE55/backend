using System.Text.Json;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Entities;
using SharedContracts.Events.Root;
using SharedContracts.Interfaces;

namespace BatteryService.Application.Services;

/// <summary>
/// Ghi <see cref="IntegrationEvent"/> vào bảng <c>outbox_messages</c> trong cùng
/// transaction với DbContext hiện tại. Background <c>OutboxRelayBackgroundService</c>
/// sẽ đọc và publish lên RabbitMQ qua <see cref="IIntegrationEventTransport"/>.
///
/// Sprint 5B #235 — tách khỏi <see cref="IIntegrationEventTransport"/>.
/// </summary>
public class IntegrationEventOutboxWriter : IIntegrationEventOutboxWriter
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public IntegrationEventOutboxWriter(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task WriteAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IntegrationEvent
    {
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            AggregateId = @event.Id,
            Type = typeof(TEvent).Name,
            Payload = JsonSerializer.Serialize<object>(@event),
            OccurredAtUtc = DateTime.UtcNow,
            RetryCount = 0
        };

        await _unitOfWork.OutboxMessages.AddAsync(message);
    }
}
