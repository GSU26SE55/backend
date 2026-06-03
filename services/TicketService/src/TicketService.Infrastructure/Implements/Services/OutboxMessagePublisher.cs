using System.Text.Json;
using SharedContracts.Events.Root;
using SharedContracts.Interfaces;
using TicketService.Domain.Entities;
using TicketService.Infrastructure.Persistence;

namespace TicketService.Infrastructure.Implements.Services;

/// <summary>
/// Publisher dùng Outbox Pattern cho TicketService.
/// Thay vì publish thẳng lên RabbitMQ, INSERT event vào bảng outbox_messages cùng DbContext.
/// </summary>
public class OutboxMessagePublisher : IMessageProducerService
{
    private readonly TicketDbContext _dbContext;

    public OutboxMessagePublisher(TicketDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default) where T : IntegrationEvent
    {
        var outbox = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = typeof(T).Name,
            Payload = JsonSerializer.Serialize(message, message.GetType()),
            OccurredAtUtc = message.OccurredAt,
            ProcessedAtUtc = null,
            RetryCount = 0,
            LastError = null,
            AggregateId = Guid.Empty // Có thể mở rộng sau nếu cần tracking aggregate cụ thể
        };

        _dbContext.OutboxMessages.Add(outbox);
        return Task.CompletedTask;
    }
}
