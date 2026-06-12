using SharedContracts.Events.Root;

namespace SharedContracts.Interfaces;

/// <summary>
/// Ghi integration event vào outbox table (transactional với DbContext hiện tại).
/// Tách khỏi <see cref="IMessageProducerService"/> để giúp test/mock độc lập
/// giữa: viết outbox (in-transaction) vs publish lên bus (post-commit).
///
/// Sprint 5B #235 — DI split (xem overall.md §53.5).
/// </summary>
public interface IIntegrationEventOutboxWriter
{
    /// <summary>
    /// Serialize và lưu event vào bảng <c>outbox_messages</c> của service hiện tại
    /// trong cùng transaction với DbContext. Không publish lên bus.
    /// </summary>
    Task WriteAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IntegrationEvent;
}

/// <summary>
/// Publish integration event lên message bus (RabbitMQ).
/// Sử dụng sau khi transaction đã commit (outside DbContext scope).
/// Tách khỏi <see cref="IIntegrationEventOutboxWriter"/>.
///
/// Sprint 5B #235 — DI split.
/// </summary>
public interface IIntegrationEventTransport
{
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IntegrationEvent;
}
