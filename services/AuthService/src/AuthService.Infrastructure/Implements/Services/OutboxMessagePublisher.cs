using System.Text.Json;
using AuthService.Domain.Entities;
using AuthService.Infrastructure.Persistence;
using SharedContracts.Events.Root;
using SharedContracts.Interfaces;

namespace AuthService.Infrastructure.Implements.Services;

/// <summary>
/// Publisher dùng Outbox Pattern: thay vì publish thẳng lên RabbitMQ, INSERT event vào bảng outbox_messages
/// trong cùng DbContext của handler. Khi handler gọi SaveChangesAsync, event và business data được commit
/// atomic. OutboxRelayBackgroundService sẽ đọc và publish thật lên RabbitMQ sau.
/// </summary>
public class OutboxMessagePublisher : IMessageProducerService
{
    private readonly ApplicationDbContext _dbContext;

    public OutboxMessagePublisher(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default) where T : IntegrationEvent
    {
        var outbox = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = typeof(T).AssemblyQualifiedName ?? typeof(T).FullName ?? typeof(T).Name,
            Payload = JsonSerializer.Serialize(message, message.GetType()),
            OccurredAt = message.OccurredAt,
            ProcessedAt = null,
            RetryCount = 0,
            LastError = null
        };

        _dbContext.OutboxMessages.Add(outbox);
        return Task.CompletedTask;
    }
}
