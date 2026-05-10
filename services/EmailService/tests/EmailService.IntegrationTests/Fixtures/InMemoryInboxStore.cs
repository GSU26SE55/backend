using System.Collections.Concurrent;
using SharedInfrastructure.Idempotency;

namespace EmailService.IntegrationTests.Fixtures;

public class InMemoryInboxStore : IInboxStore
{
    private readonly ConcurrentDictionary<string, byte> _processedMessages = new();

    public Task<bool> TryMarkProcessedAsync(
        Guid messageId,
        string consumerName,
        CancellationToken cancellationToken = default)
    {
        var key = $"{consumerName}:{messageId:N}";
        return Task.FromResult(_processedMessages.TryAdd(key, 0));
    }
}
