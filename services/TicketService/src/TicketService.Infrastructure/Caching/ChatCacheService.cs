using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Infrastructure.Caching;

public class ChatCacheService : IChatCacheService
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);
    private readonly ICacheService _cache;

    public ChatCacheService(ICacheService cache)
    {
        _cache = cache;
    }

    private static string Key(Guid ticketId, int pageNumber, int pageSize, bool canViewInternal)
        => $"ticket:{ticketId}:chats:p{pageNumber}:s{pageSize}:{(canViewInternal ? "full" : "pub")}";

    public Task<CachedChatPage?> GetPageAsync(Guid ticketId, int pageNumber, int pageSize, bool canViewInternal, CancellationToken ct = default)
        => _cache.GetAsync<CachedChatPage>(Key(ticketId, pageNumber, pageSize, canViewInternal), ct);

    public Task SetPageAsync(Guid ticketId, int pageNumber, int pageSize, bool canViewInternal, List<TicketChatDTO> chats, int totalItems, CancellationToken ct = default)
        => _cache.SetAsync(Key(ticketId, pageNumber, pageSize, canViewInternal), new CachedChatPage(chats, totalItems), Ttl, ct);

    public Task InvalidateAsync(Guid ticketId, CancellationToken ct = default)
        // IDistributedCache không hỗ trợ pattern delete — xóa cả 2 variant (full/pub) của page 1 default.
        => Task.WhenAll(
            _cache.RemoveAsync(Key(ticketId, 1, 10, true), ct),
            _cache.RemoveAsync(Key(ticketId, 1, 10, false), ct));
}
