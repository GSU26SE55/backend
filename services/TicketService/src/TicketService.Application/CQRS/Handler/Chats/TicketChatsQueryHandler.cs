using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.Common.Helpers;
using TicketService.Application.CQRS.Query.Ticket;
using TicketService.Application.DTOs.Response.Chats;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Helpers;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Application.CQRS.Handler.Chats;

public class TicketChatsQueryHandler : IRequestHandler<TicketChatsQuery, CommonResponse<PaginationResponse<TicketChatDTO>>>
{
    private readonly ITicketUnitOfWork _unitOfWork;
    private readonly IChatCacheService _chatCache;

    public TicketChatsQueryHandler(ITicketUnitOfWork unitOfWork, IChatCacheService chatCache)
    {
        _unitOfWork = unitOfWork;
        _chatCache = chatCache;
    }

    public async Task<CommonResponse<PaginationResponse<TicketChatDTO>>> Handle(TicketChatsQuery request, CancellationToken cancellationToken)
    {
        // 1. Kiểm tra ticket có tồn tại không và check quyền truy cập ticket
        var ticket = await _unitOfWork.Tickets.GetAllAsync()
            .AsNoTracking()
            .Where(t => t.Id == request.TicketId && !t.IsDeleted)
            .Select(t => new { t.CustomerId, t.AssignedStaffId })
            .FirstOrDefaultAsync(cancellationToken);

        if (ticket is null)
            return new CommonResponse<PaginationResponse<TicketChatDTO>> { IsSuccess = false, StatusCode = 404, Message = "Ticket not found" };

        var activeParticipants = await _unitOfWork.TicketParticipants.GetAllAsync()
            .AsNoTracking()
            .Where(p => p.TicketId == request.TicketId && p.RemovedAt == null && !p.IsDeleted)
            .Select(p => new { p.UserId, p.CanViewInternal })
            .ToListAsync(cancellationToken);

        if (!TicketQueryHelper.CanAccessTicket(ticket.CustomerId, ticket.AssignedStaffId, request.ActorUserId, request.ActorRoles, activeParticipants.Select(p => p.UserId).ToList()))
            return new CommonResponse<PaginationResponse<TicketChatDTO>> { IsSuccess = false, StatusCode = 403, Message = "Forbidden" };

        // 2. Xác định xem actor có quyền xem chat nội bộ không
        var participantCanViewInternal = activeParticipants.Any(p => p.UserId == request.ActorUserId && p.CanViewInternal);
        var canViewInternalChats = TicketQueryHelper.CanViewInternalChats(request.ActorRoles, participantCanViewInternal);

        // Cache hit — chỉ khi page 1, pageSize default (10), không có filter nào.
        // canViewInternalChats phải vào key để tránh Customer thấy internal chats từ cache của Staff.
        var isDefaultQuery = request.PageNumber == 1
            && request.PageSize == 10
            && string.IsNullOrWhiteSpace(request.Search)
            && request.AuthorUserId == null
            && request.AuthorRole == null
            && request.IsInternal == null
            && request.IsPinned == null
            && request.HasAttachments == null
            && request.MentionedMe == null
            && request.DateFrom == null
            && request.DateTo == null;

        if (isDefaultQuery)
        {
            var cached = await _chatCache.GetPageAsync(request.TicketId, 1, request.PageSize, canViewInternalChats, cancellationToken);
            if (cached != null)
            {
                return new CommonResponse<PaginationResponse<TicketChatDTO>>
                {
                    IsSuccess = true,
                    StatusCode = 200,
                    Data = new PaginationResponse<TicketChatDTO>
                    {
                        Items = cached.Items,
                        TotalItems = cached.TotalItems,
                        PageNumber = 1,
                        PageSize = request.PageSize
                    }
                };
            }
        }

        // 3. Query chats
        var query = _unitOfWork.TicketChats.GetAllAsync()
            .AsNoTracking()
            .Where(c => c.TicketId == request.TicketId && !c.IsDeleted);

        // 4. Lọc chat nội bộ nếu là Customer
        if (!canViewInternalChats)
            query = query.Where(c => !c.IsInternal);

        // #549 — Extended filters
        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(c => c.Body.Contains(request.Search));

        if (request.AuthorUserId.HasValue)
            query = query.Where(c => c.AuthorUserId == request.AuthorUserId.Value);

        if (request.AuthorRole.HasValue)
            query = query.Where(c => c.AuthorRole == request.AuthorRole.Value);

        if (request.IsInternal.HasValue)
            query = query.Where(c => c.IsInternal == request.IsInternal.Value);

        if (request.IsPinned.HasValue)
            query = query.Where(c => c.IsPinned == request.IsPinned.Value);

        if (request.HasAttachments.HasValue)
        {
            query = request.HasAttachments.Value
                ? query.Where(c => c.AttachmentFileIds != null && c.AttachmentFileIds.Count > 0)
                : query.Where(c => c.AttachmentFileIds == null || c.AttachmentFileIds.Count == 0);
        }

        if (request.MentionedMe == true)
        {
            var mentionedChatIds = _unitOfWork.TicketChatMentions.GetAllAsync()
                .AsNoTracking()
                .Where(m => m.MentionedUserId == request.ActorUserId && !m.IsDeleted)
                .Select(m => m.ChatId);
            query = query.Where(c => mentionedChatIds.Contains(c.Id));
        }

        if (request.DateFrom.HasValue)
            query = query.Where(c => c.CreatedAt >= request.DateFrom.Value);

        if (request.DateTo.HasValue)
            query = query.Where(c => c.CreatedAt <= request.DateTo.Value);

        var total = await query.CountAsync(cancellationToken);
        var rawChats = await query
            .OrderByDescending(c => c.IsPinned)
            .ThenByDescending(c => c.CreatedAt)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var chatIds = rawChats.Select(c => c.Id).ToList();
        var mentionsByChat = await ChatChildDataLoader.LoadMentionsAsync(_unitOfWork, chatIds, cancellationToken);
        var reactionsByChat = await ChatChildDataLoader.LoadReactionsAsync(_unitOfWork, chatIds, cancellationToken);

        var items = rawChats.Select(c => new TicketChatDTO
        {
            Id = c.Id.ToString(),
            TicketId = c.TicketId.ToString(),
            AuthorUserId = c.AuthorUserId.ToString(),
            AuthorRole = c.AuthorRole,
            AuthorDisplayName = c.AuthorDisplayName,
            Body = c.Body,
            IsInternal = c.IsInternal,
            AttachmentFileIds = c.AttachmentFileIds.Select(id => id.ToString()).ToList(),
            CreatedAt = c.CreatedAt,
            BodyFormat = c.BodyFormat,
            BodyHtml = c.BodyHtml,
            ParentChatId = c.ParentChatId?.ToString(),
            ThreadRootId = c.ThreadRootId?.ToString(),
            ReplyCount = c.ReplyCount,
            IsPinned = c.IsPinned,
            PinnedAt = c.PinnedAt,
            PinnedByUserId = c.PinnedByUserId?.ToString(),
            Mentions = mentionsByChat.TryGetValue(c.Id, out var m) ? m : new(),
            Reactions = reactionsByChat.TryGetValue(c.Id, out var r) ? r : new TicketChatReactionsAggregateDTO()
        }).ToList();

        if (isDefaultQuery)
            await _chatCache.SetPageAsync(request.TicketId, 1, request.PageSize, canViewInternalChats, items, total, cancellationToken);

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
