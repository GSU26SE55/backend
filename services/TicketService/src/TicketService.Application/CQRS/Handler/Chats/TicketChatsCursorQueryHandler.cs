using System.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.Common.Utils;
using TicketService.Application.CQRS.Query.Ticket;
using TicketService.Application.DTOs.Response.Chats;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;

namespace TicketService.Application.CQRS.Handler.Chats;

public class TicketChatsCursorQueryHandler : IRequestHandler<TicketChatsCursorQuery, CommonResponse<CursorPaginationResponse<TicketChatDTO>>>
{
    private readonly ITicketUnitOfWork _uow;

    public TicketChatsCursorQueryHandler(ITicketUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<CommonResponse<CursorPaginationResponse<TicketChatDTO>>> Handle(TicketChatsCursorQuery request, CancellationToken ct)
    {
        var ticket = await _uow.Tickets.GetAllAsync()
            .AsNoTracking()
            .Where(t => t.Id == request.TicketId && !t.IsDeleted)
            .Select(t => new { t.CustomerId, t.AssignedStaffId })
            .FirstOrDefaultAsync(ct);

        if (ticket is null)
            return Fail(404, "Ticket not found");

        var activeParticipants = await _uow.TicketParticipants.GetAllAsync()
            .AsNoTracking()
            .Where(p => p.TicketId == request.TicketId && p.RemovedAt == null && !p.IsDeleted)
            .Select(p => new { p.UserId, p.CanViewInternal })
            .ToListAsync(ct);

        if (!TicketQueryHelper.CanAccessTicket(ticket.CustomerId, ticket.AssignedStaffId, request.ActorUserId, request.ActorRoles, activeParticipants.Select(p => p.UserId).ToList()))
            return Fail(403, "Forbidden");

        var participantCanViewInternal = activeParticipants.Any(p => p.UserId == request.ActorUserId && p.CanViewInternal);
        var canViewInternal = TicketQueryHelper.CanViewInternalChats(request.ActorRoles, participantCanViewInternal);

        var query = _uow.TicketChats.GetAllAsync()
            .AsNoTracking()
            .Where(c => c.TicketId == request.TicketId && !c.IsDeleted);

        if (!canViewInternal)
            query = query.Where(c => !c.IsInternal);

        // Cursor composite: base64("{chatId}:{createdAtTicks}") — không cần extra DB round-trip
        if (!string.IsNullOrEmpty(request.Cursor) && TryDecodeCursor(request.Cursor, out var cursorChatId, out var cursorCreatedAt))
        {
            query = query.Where(c => c.CreatedAt < cursorCreatedAt
                || (c.CreatedAt == cursorCreatedAt && c.Id != cursorChatId));
        }

        var limit = Math.Clamp(request.Limit, 1, 100);
        var rawChats = await query
            .OrderByDescending(c => c.CreatedAt)
            .Take(limit + 1)
            .ToListAsync(ct);

        var hasMore = rawChats.Count > limit;
        if (hasMore)
            rawChats.RemoveAt(rawChats.Count - 1);

        var chatIds = rawChats.Select(c => c.Id).ToList();
        var mentionsByChat = await ChatChildDataLoader.LoadMentionsAsync(_uow, chatIds, ct);
        var reactionsByChat = await ChatChildDataLoader.LoadReactionsAsync(_uow, chatIds, ct);
        var translationsByChat = await ChatChildDataLoader.LoadTranslationsForUserAsync(_uow, chatIds, request.ActorUserId, ct);

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
            PinnedAt = c.PinnedAt,
            PinnedByUserId = c.PinnedByUserId?.ToString(),
            ParentChatId = c.ParentChatId?.ToString(),
            ThreadRootId = c.ThreadRootId?.ToString(),
            ReplyCount = c.ReplyCount,
            Mentions = mentionsByChat.TryGetValue(c.Id, out var m) ? m : new(),
            Reactions = reactionsByChat.TryGetValue(c.Id, out var r) ? r : new TicketChatReactionsAggregateDTO(),
            ActiveTranslation = translationsByChat.TryGetValue(c.Id, out var tr) ? tr : null,
        }).ToList();

        var nextCursor = hasMore ? EncodeCursor(rawChats.Last().Id, rawChats.Last().CreatedAt) : null;

        return new CommonResponse<CursorPaginationResponse<TicketChatDTO>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new CursorPaginationResponse<TicketChatDTO>
            {
                Items = items,
                NextCursor = nextCursor,
                HasMore = hasMore
            }
        };
    }

    private static CommonResponse<CursorPaginationResponse<TicketChatDTO>> Fail(int statusCode, string message)
        => new() { IsSuccess = false, StatusCode = statusCode, Message = message };

    private static string EncodeCursor(Guid chatId, DateTime createdAt)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes($"{chatId}:{createdAt.Ticks}"));

    private static bool TryDecodeCursor(string cursor, out Guid chatId, out DateTime createdAt)
    {
        chatId = Guid.Empty;
        createdAt = default;
        try
        {
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parts = raw.Split(':', 2);
            if (parts.Length == 2 && Guid.TryParse(parts[0], out chatId) && long.TryParse(parts[1], out var ticks))
            {
                createdAt = new DateTime(ticks, DateTimeKind.Utc);
                return true;
            }
        }
        catch { /* invalid cursor — ignore, treat as first page */ }
        return false;
    }
}
