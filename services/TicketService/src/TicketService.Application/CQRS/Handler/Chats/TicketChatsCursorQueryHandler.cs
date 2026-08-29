using System.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.Common.Utils;
using TicketService.Application.CQRS.Query.Ticket;
using TicketService.Application.DTOs.Response.Chats;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Enums;

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
            .Select(t => new { t.CustomerId, PrimaryHandlerStaffId = t.Assignments.Where(a => !a.IsDeleted && a.Role == AssignmentRoleEnum.PrimaryHandler).Select(a => (Guid?)a.StaffId).FirstOrDefault() })
            .FirstOrDefaultAsync(ct);

        if (ticket is null)
            return Fail(404, "Ticket not found");

        var activeParticipants = await _uow.TicketParticipants.GetAllAsync()
            .AsNoTracking()
            .Where(p => p.TicketId == request.TicketId && p.RemovedAt == null && !p.IsDeleted)
            .Select(p => new { p.UserId, p.CanViewInternal })
            .ToListAsync(ct);

        if (!TicketQueryHelper.CanAccessTicket(ticket.CustomerId, ticket.PrimaryHandlerStaffId, request.ActorUserId, request.ActorRoles, activeParticipants.Select(p => p.UserId).ToList()))
            return Fail(403, "Forbidden");

        var participantCanViewInternal = activeParticipants.Any(p => p.UserId == request.ActorUserId && p.CanViewInternal);
        var canViewInternal = TicketQueryHelper.CanViewInternalChats(request.ActorRoles, participantCanViewInternal);

        var hiddenChatIdsQuery = _uow.TicketChatHides.GetAllAsync()
            .Where(h => h.UserId == request.ActorUserId && !h.IsDeleted)
            .Select(h => h.ChatId);

        var query = _uow.TicketChats.GetAllAsync()
            .AsNoTracking()
            .Where(c => c.TicketId == request.TicketId && !hiddenChatIdsQuery.Contains(c.Id));

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

        var nonDeletedIds = rawChats.Where(c => !c.IsDeleted).Select(c => c.Id).ToList();
        var mentionsByChat = await ChatChildDataLoader.LoadMentionsAsync(_uow, nonDeletedIds, ct);
        var reactionsByChat = await ChatChildDataLoader.LoadReactionsAsync(_uow, nonDeletedIds, ct);
        var translationsByChat = await ChatChildDataLoader.LoadTranslationsForUserAsync(_uow, nonDeletedIds, request.ActorUserId, ct);
        // Mốc "Tin nhắn chưa đọc" ở client cần biết từng tin đã đọc hay chưa.
        // Tin của chính actor luôn tính là đã đọc — BE không ghi read-receipt cho tác giả.
        var readChatIds = await ChatChildDataLoader.LoadReadChatIdsForUserAsync(
            _uow, rawChats.Select(c => c.Id).ToList(), request.ActorUserId, ct);
        // "Đã xem" kiểu Messenger — chỉ nạp cho tin do CHÍNH actor gửi.
        var ownChatIds = rawChats
            .Where(c => c.AuthorUserId == request.ActorUserId && !c.IsDeleted)
            .Select(c => c.Id)
            .ToList();
        var receiptsByChat = await ChatChildDataLoader.LoadReadReceiptsAsync(_uow, ownChatIds, ct);

        var items = rawChats.Select(c =>
        {
            var receipts = receiptsByChat.TryGetValue(c.Id, out var rr) ? rr : new List<ChatReaderDTO>();
            return new TicketChatDTO
            {
                Id = c.Id.ToString(),
                TicketId = c.TicketId.ToString(),
                AuthorUserId = c.AuthorUserId.ToString(),
                IsRead = c.AuthorUserId == request.ActorUserId || readChatIds.Contains(c.Id),
                ReadReceipts = receipts,
                ReadCount = receipts.Count,
                AuthorRole = c.AuthorRole,
                AuthorDisplayName = c.AuthorDisplayName,
                IsInternal = c.IsInternal,
                CreatedAt = c.CreatedAt,
                IsPinned = c.IsPinned,
                PinnedAt = c.PinnedAt,
                PinnedByUserId = c.PinnedByUserId?.ToString(),
                ParentChatId = c.ParentChatId?.ToString(),
                ThreadRootId = c.ThreadRootId?.ToString(),
                ReplyCount = c.ReplyCount,
                IsDeleted = c.IsDeleted,
                Body = c.IsDeleted ? "This message has been deleted." : c.Body,
                BodyHtml = c.IsDeleted ? null : c.BodyHtml,
                BodyFormat = c.IsDeleted ? default : c.BodyFormat,
                AttachmentFileIds = c.IsDeleted ? [] : c.AttachmentFileIds.Select(id => id.ToString()).ToList(),
                EditedAt = c.IsDeleted ? null : c.EditedAt,
                EditCount = c.IsDeleted ? 0 : c.EditCount,
                LastEditedByUserId = c.IsDeleted ? null : c.LastEditedByUserId?.ToString(),
                Mentions = c.IsDeleted ? [] : (mentionsByChat.TryGetValue(c.Id, out var m) ? m : []),
                Reactions = c.IsDeleted ? new() : (reactionsByChat.TryGetValue(c.Id, out var r) ? r : new TicketChatReactionsAggregateDTO()),
                ActiveTranslation = c.IsDeleted ? null : (translationsByChat.TryGetValue(c.Id, out var tr) ? tr : null),
            };
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
