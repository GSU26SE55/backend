using System.Collections.Concurrent;
using SharedContracts.Interfaces;

namespace TicketService.IntergrationTests.Fixtures;

/// <summary>
/// In-memory ICacheService cho integration test — production dùng RedisCacheService,
/// không sẵn trong test env (#518 SpamDetector phụ thuộc ICacheService).
/// </summary>
internal sealed class StubCacheService : ICacheService
{
    private readonly ConcurrentDictionary<string, object?> _store = new();

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        => Task.FromResult(_store.TryGetValue(key, out var value) ? (T?)value : default);

    public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        _store[key] = value;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _store.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
