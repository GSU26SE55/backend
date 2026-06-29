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
    private const int MaxRepeats = 3;

    private readonly ICacheService _cache;

    public SpamDetector(ICacheService cache)
    {
        _cache = cache;
    }

    public async Task<bool> IsSpamAsync(Guid ticketId, Guid userId, string body, CancellationToken cancellationToken = default)
    {
        var key = $"chat:spam:{ticketId}:{userId}";
        var entries = await _cache.GetAsync<List<SpamEntry>>(key, cancellationToken) ?? new List<SpamEntry>();

        var now = DateTime.UtcNow;
        entries = entries.Where(e => now - e.PostedAt <= Window).ToList();

        var bodyHash = ComputeHash(body);
        var priorRepeatCount = entries.Count(e => e.BodyHash == bodyHash);

        entries.Add(new SpamEntry(bodyHash, now));
        await _cache.SetAsync(key, entries, Window, cancellationToken);

        // priorRepeatCount là số lần TRƯỚC đó — lần post hiện tại là lần thứ (priorRepeatCount + 1).
        return priorRepeatCount + 1 >= MaxRepeats;
    }

    private static string ComputeHash(string body)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(body));
        return Convert.ToHexString(bytes);
    }

    private sealed record SpamEntry(string BodyHash, DateTime PostedAt);
}
