using MassTransit;
using SharedContracts.Events.Root;
using SharedInfrastructure.Metrics;

namespace SharedInfrastructure.Idempotency;

public static class IdempotentConsumerExtensions
{
    /// <summary>
    /// Wrap consumer logic with Inbox idempotency. Action chỉ chạy 1 lần cho mỗi (messageId, consumerName).
    /// Trả về true nếu action đã chạy, false nếu skip (duplicate).
    /// </summary>
    public static async Task<bool> ProcessOnceAsync<TMessage>(
        this ConsumeContext<TMessage> context,
        IInboxStore store,
        string consumerName,
        Func<Task> action)
        where TMessage : class
    {
        var messageId = context.Message switch
        {
            IntegrationEvent evt => evt.Id,
            _ => context.MessageId ?? Guid.NewGuid()
        };

        var firstTime = await store.TryMarkProcessedAsync(messageId, consumerName, context.CancellationToken);
        if (!firstTime)
        {
            AppMetrics.InboxSkippedDuplicate.WithLabels(consumerName).Inc();
            return false;
        }

        await action();
        AppMetrics.InboxProcessed.WithLabels(consumerName).Inc();
        return true;
    }
}
