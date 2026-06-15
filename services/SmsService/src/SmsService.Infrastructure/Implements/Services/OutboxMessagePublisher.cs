using System.Text.Json;
using SharedContracts.Events.Root;
using SharedContracts.Interfaces;
using SmsService.Domain.Entities;
using SmsService.Infrastructure.Persistence;

namespace SmsService.Infrastructure.Implements.Services;

/// <summary>
/// Outbox Pattern publisher: thay vì publish thẳng RabbitMQ, INSERT row vào
/// <c>SmsDbContext.OutboxMessages</c>. Handler gọi <c>SaveChangesAsync</c> sau →
/// business data và event atomic. <c>OutboxRelayBackgroundService</c> sẽ poll bảng
/// outbox và publish thật lên RabbitMQ sau.
/// <para>
/// Đăng ký SAU <c>AddMessageBus</c> để override <c>IMessageProducerService</c> default (<c>MassTransitProducer</c>).
/// </para>
/// </summary>
public class OutboxMessagePublisher : IMessageProducerService
{
    private readonly SmsDbContext _dbContext;

    public OutboxMessagePublisher(SmsDbContext dbContext)
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
