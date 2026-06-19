using System.Collections.Concurrent;
using AuthService.Application.Interfaces.Services;

namespace AuthService.IntegrationTests.Fixtures;

/// <summary>
/// In-memory replacement cho Redis-backed <see cref="ITwoFactorChallengeStore"/> trong integration test.
/// TTL không enforce strictly — test tự reset giữa các run qua <see cref="Clear"/>.
/// </summary>
public class InMemoryTwoFactorChallengeStore : ITwoFactorChallengeStore
{
    private readonly ConcurrentDictionary<string, ChallengeEntry> _entries = new();

    public Task<string> CreateAsync(Guid accountId, string ipAddress, string userAgent, TimeSpan ttl, CancellationToken ct = default)
    {
        var token = Guid.NewGuid().ToString("N");
        _entries[token] = new ChallengeEntry(accountId, ipAddress, userAgent, 0, DateTime.UtcNow.Add(ttl));
        return Task.FromResult(token);
    }

    public Task<TwoFactorChallengeData?> GetAsync(string token, CancellationToken ct = default)
    {
        if (!_entries.TryGetValue(token, out var entry))
            return Task.FromResult<TwoFactorChallengeData?>(null);
        if (entry.ExpiresAt < DateTime.UtcNow)
        {
            _entries.TryRemove(token, out _);
            return Task.FromResult<TwoFactorChallengeData?>(null);
        }
        return Task.FromResult<TwoFactorChallengeData?>(new TwoFactorChallengeData(entry.AccountId, entry.IpAddress, entry.UserAgent, entry.Attempts, entry.ExpiresAt.AddSeconds(-300)));
    }

    public Task<int> IncrementAttemptsAsync(string token, CancellationToken ct = default)
    {
        if (!_entries.TryGetValue(token, out var entry))
            return Task.FromResult(0);
        var updated = entry with { Attempts = entry.Attempts + 1 };
        _entries[token] = updated;
        return Task.FromResult(updated.Attempts);
    }

    public Task InvalidateAsync(string token, CancellationToken ct = default)
    {
        _entries.TryRemove(token, out _);
        return Task.CompletedTask;
    }

    public Task InvalidateByAccountAsync(Guid accountId, CancellationToken ct = default)
    {
        foreach (var kvp in _entries)
        {
            if (kvp.Value.AccountId == accountId)
                _entries.TryRemove(kvp.Key, out _);
        }
        return Task.CompletedTask;
    }

    public void Clear() => _entries.Clear();

    private sealed record ChallengeEntry(Guid AccountId, string IpAddress, string UserAgent, int Attempts, DateTime ExpiresAt);
}
