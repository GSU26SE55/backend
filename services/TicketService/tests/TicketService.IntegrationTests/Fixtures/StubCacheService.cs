using System.Collections.Concurrent;
using SharedContracts.Interfaces;

namespace TicketService.IntegrationTests.Fixtures;

/// <summary>
/// In-memory ICacheService cho integration test — production dùng RedisCacheService,
/// không sẵn trong test env (#518 SpamDetector phụ thuộc ICacheService).
/// </summary>
internal sealed class StubCacheService : ICacheService
{
    private readonly ConcurrentDictionary<string, object?> _store = new();
    private readonly object _leaseLock = new();

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

    /// <summary>
    /// Sprint 6.3 NOTI3-09 (#709). <c>TryAdd</c> của ConcurrentDictionary vốn atomic nên stub này
    /// mô phỏng đúng ngữ nghĩa SET NX của Redis (khác bản Redis ở chỗ không áp dụng TTL).
    /// </summary>
    public Task<bool> TrySetIfNotExistsAsync(
        string key, string value, TimeSpan expiration, CancellationToken cancellationToken = default)
        => Task.FromResult(_store.TryAdd(key, value));

    /// <summary>Sprint 6.3 NOTI3-06 (#706) — bộ đếm in-memory cho rate limit.</summary>
    public Task<long> IncrementAsync(
        string key, TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        // _store lưu object nên đọc lại phải quy về long thay vì parse chuỗi.
        var next = _store.TryGetValue(key, out var v) && v is long current ? current + 1 : 1L;
        _store[key] = next;
        return Task.FromResult(next);
    }

    public Task<long?> GetCounterAsync(string key, CancellationToken cancellationToken = default)
        => Task.FromResult(_store.TryGetValue(key, out var value) && value is long count
            ? (long?)count
            : null);

    public Task<bool> TryRefreshLeaseAsync(
        string key, string ownerToken, TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        lock (_leaseLock)
        {
            // The in-memory stub has no TTL, but preserves owner-token comparison semantics.
            return Task.FromResult(_store.TryGetValue(key, out var value)
                && string.Equals(value as string, ownerToken, StringComparison.Ordinal));
        }
    }

    public Task<bool> TryReleaseLeaseAsync(
        string key, string ownerToken, CancellationToken cancellationToken = default)
    {
        lock (_leaseLock)
        {
            if (!_store.TryGetValue(key, out var value)
                || !string.Equals(value as string, ownerToken, StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }

            return Task.FromResult(_store.TryRemove(key, out _));
        }
    }
}
