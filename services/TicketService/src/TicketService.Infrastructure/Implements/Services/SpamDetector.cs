using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SharedContracts.Interfaces;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Infrastructure.Implements.Services;

/// <summary>
/// Sliding window 5 phút dùng <see cref="ICacheService"/> (Redis) — fetch/prune/append theo
/// cặp (ticketId, userId), không atomic 100% dưới concurrency cao nhưng đủ cho scope spam-guard (#518).
/// </summary>
public class SpamDetector : ISpamDetector
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LeaseTtl = TimeSpan.FromSeconds(30);
    private const int MaxRepeats = 3;

    private readonly ICacheService _cache;

    public SpamDetector(ICacheService cache)
    {
        _cache = cache;
    }

    public async Task<SpamLease?> TryAcquireLeaseAsync(Guid ticketId, Guid userId, CancellationToken cancellationToken = default)
    {
        var key = $"ticket-chat:spam:lock:{ticketId}:{userId}";
        var token = Guid.NewGuid().ToString("N");
        return await _cache.TrySetIfNotExistsAsync(key, token, LeaseTtl, cancellationToken)
            ? new SpamLease(key, token)
            : null;
    }

    public Task<bool> RenewLeaseAsync(SpamLease lease, CancellationToken cancellationToken = default)
        => _cache.TryRefreshLeaseAsync(lease.Key, lease.OwnerToken, LeaseTtl, cancellationToken);

    public async Task ReleaseLeaseAsync(SpamLease lease, CancellationToken cancellationToken = default)
    {
        await _cache.TryReleaseLeaseAsync(lease.Key, lease.OwnerToken, cancellationToken);
    }

    public async Task<bool> IsSpamAsync(Guid ticketId, Guid userId, string body, CancellationToken cancellationToken = default)
    {
        var key = $"chat:spam:{ticketId}:{userId}";
        var entries = await _cache.GetAsync<List<SpamEntry>>(key, cancellationToken) ?? new List<SpamEntry>();

        var now = DateTime.UtcNow;
        entries = entries.Where(e => now - e.PostedAt <= Window).ToList();

        var bodyHash = ComputeHash(body);
        var priorRepeatCount = entries.Count(e => e.BodyHash == bodyHash);

        return priorRepeatCount + 1 >= MaxRepeats;
    }

    public async Task RecordAcceptedMessageAsync(Guid ticketId, Guid userId, string body, CancellationToken cancellationToken = default)
    {
        var key = $"chat:spam:{ticketId}:{userId}";
        var entries = await _cache.GetAsync<List<SpamEntry>>(key, cancellationToken) ?? new List<SpamEntry>();
        var now = DateTime.UtcNow;
        entries = entries.Where(e => now - e.PostedAt <= Window).ToList();
        entries.Add(new SpamEntry(ComputeHash(body), now));
        await _cache.SetAsync(key, entries, Window, cancellationToken);
    }

    private static string ComputeHash(string body)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(body));
        return Convert.ToHexString(bytes);
    }

    private record SpamEntry(string BodyHash, DateTime PostedAt);
}
