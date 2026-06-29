using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TicketService.Application.DTOs.Response.Tickets;

namespace TicketService.Application.Interfaces.Services;

public record CachedChatPage(List<TicketChatDTO> Items, int TotalItems);

public interface IChatCacheService
{
    Task<CachedChatPage?> GetPageAsync(Guid ticketId, int pageNumber, int pageSize, bool canViewInternal, CancellationToken ct = default);
    Task SetPageAsync(Guid ticketId, int pageNumber, int pageSize, bool canViewInternal, List<TicketChatDTO> chats, int totalItems, CancellationToken ct = default);
    Task InvalidateAsync(Guid ticketId, CancellationToken ct = default);
}
