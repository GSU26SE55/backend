using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.Common.Helpers;
using TicketService.Application.CQRS.Query.ChatGlobalSearch;
using TicketService.Application.DTOs.Response.Chats;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Helpers;
using TicketService.Application.Interfaces.Repositories;

namespace TicketService.Application.CQRS.Handler.Chats;

public class ChatGlobalSearchQueryHandler : IRequestHandler<ChatGlobalSearchQuery, CommonResponse<PaginationResponse<TicketChatDTO>>>
{
    private readonly ITicketUnitOfWork _uow;

    public ChatGlobalSearchQueryHandler(ITicketUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<CommonResponse<PaginationResponse<TicketChatDTO>>> Handle(ChatGlobalSearchQuery request, CancellationToken ct)
    {
        var canViewInternal = TicketQueryHelper.CanViewInternalChats(request.ActorRoles);

        var query = _uow.TicketChats.GetAllAsync()
            .AsNoTracking()
            .Where(c => !c.IsDeleted);

        if (!canViewInternal)
            query = query.Where(c => !c.IsInternal);

        if (!string.IsNullOrWhiteSpace(request.Q))
            query = query.Where(c => c.Body.Contains(request.Q));

        if (request.CustomerId.HasValue)
        {
            var ticketIds = _uow.Tickets.GetAllAsync()
                .AsNoTracking()
                .Where(t => t.CustomerId == request.CustomerId.Value && !t.IsDeleted)
                .Select(t => t.Id);
            query = query.Where(c => ticketIds.Contains(c.TicketId));
        }

        if (request.DateFrom.HasValue)
            query = query.Where(c => c.CreatedAt >= request.DateFrom.Value);

        if (request.DateTo.HasValue)
            query = query.Where(c => c.CreatedAt <= request.DateTo.Value);

        if (request.AuthorRole.HasValue)
            query = query.Where(c => c.AuthorRole == request.AuthorRole.Value);

        if (request.IsInternal.HasValue)
            query = query.Where(c => c.IsInternal == request.IsInternal.Value);

        var total = await query.CountAsync(ct);
        var rawChats = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(ct);

        var chatIds = rawChats.Select(c => c.Id).ToList();
        var mentionsByChat = await ChatChildDataLoader.LoadMentionsAsync(_uow, chatIds, ct);
        var reactionsByChat = await ChatChildDataLoader.LoadReactionsAsync(_uow, chatIds, ct);

        var items = rawChats.Select(c => new TicketChatDTO
        {
            Id = c.Id.ToString(),
            TicketId = c.TicketId.ToString(),
            AuthorUserId = c.AuthorUserId.ToString(),
            AuthorRole = c.AuthorRole,
            AuthorDisplayName = c.AuthorDisplayName,
            Body = c.Body,
            BodyHtml = c.BodyHtml,
            BodyFormat = c.BodyFormat,
            IsInternal = c.IsInternal,
            AttachmentFileIds = c.AttachmentFileIds.Select(id => id.ToString()).ToList(),
            CreatedAt = c.CreatedAt,
            EditedAt = c.EditedAt,
            IsPinned = c.IsPinned,
            Mentions = mentionsByChat.TryGetValue(c.Id, out var m) ? m : new(),
            Reactions = reactionsByChat.TryGetValue(c.Id, out var r) ? r : new TicketChatReactionsAggregateDTO()
        }).ToList();

        return new CommonResponse<PaginationResponse<TicketChatDTO>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new PaginationResponse<TicketChatDTO>
            {
                Items = items,
                TotalItems = total,
                PageNumber = request.PageNumber,
                PageSize = request.PageSize
            }
        };
    }
}
