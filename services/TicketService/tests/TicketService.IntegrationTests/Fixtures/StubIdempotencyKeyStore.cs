using System.Collections.Concurrent;
using SharedInfrastructure.Idempotency;

namespace TicketService.IntegrationTests.Fixtures;

/// <summary>
/// In-memory IIdempotencyKeyStore cho integration test.
/// Reserve trả về true lần đầu cho mỗi key, false các lần sau.
/// </summary>
internal sealed class StubIdempotencyKeyStore : IIdempotencyKeyStore
{
    private readonly ConcurrentDictionary<string, byte> _keys = new();
    private readonly ConcurrentDictionary<string, CachedIdempotencyResponse> _responses = new();

    public Task<bool> TryReserveAsync(string key, TimeSpan ttl, CancellationToken cancellationToken = default)
        => Task.FromResult(_keys.TryAdd(key, 0));

    public Task SaveResponseAsync(string key, int statusCode, string body, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        _responses[key] = new CachedIdempotencyResponse(statusCode, body);
        return Task.CompletedTask;
    }

    public Task<CachedIdempotencyResponse?> TryGetResponseAsync(string key, CancellationToken cancellationToken = default)
        => Task.FromResult(_responses.TryGetValue(key, out var r) ? r : null);
}
