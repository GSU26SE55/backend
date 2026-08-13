using System.Text.Json;
using SharedContracts.Events.Root;
using SharedContracts.Interfaces;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;

namespace TicketService.Infrastructure.Implements.Services;

/// <summary>
/// Ghi <see cref="IntegrationEvent"/> vào bảng <c>outbox_messages</c> trong cùng
/// transaction với DbContext hiện tại. Background <c>OutboxRelayBackgroundService</c>
/// sẽ đọc và publish lên RabbitMQ qua <see cref="IIntegrationEventTransport"/>.
///
/// Sprint 5B #235 — tách khỏi <see cref="IIntegrationEventTransport"/>.
/// </summary>
public class IntegrationEventOutboxWriter : IIntegrationEventOutboxWriter
{
    private readonly ITicketUnitOfWork _unitOfWork;

    public IntegrationEventOutboxWriter(ITicketUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task WriteAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IntegrationEvent
    {
        var message = new OutboxMessage
        {
            // Use the integration-event identity as the outbox primary key. Random events still
            // receive a random ID from IntegrationEvent; deterministic events gain a database
            // uniqueness barrier across retries and concurrent producers.
            Id = @event.Id,
            AggregateId = @event.Id,
            Type = typeof(TEvent).Name,
            Payload = JsonSerializer.Serialize<object>(@event),
            OccurredAtUtc = DateTime.UtcNow,
            RetryCount = 0
        };

        await _unitOfWork.OutboxMessages.AddAsync(message);
    }
}
