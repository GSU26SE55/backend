using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.Common.Helpers;
using TicketService.Application.CQRS.Query.Chats;
using TicketService.Application.DTOs.Response.Chats;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Helpers;
using TicketService.Application.Interfaces.Repositories;

namespace TicketService.Application.CQRS.Handler.Chats;

public class ChatRepliesQueryHandler : IRequestHandler<ChatRepliesQuery, CommonResponse<PaginationResponse<TicketChatDTO>>>
{
    private readonly ITicketUnitOfWork _unitOfWork;

    public ChatRepliesQueryHandler(ITicketUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<PaginationResponse<TicketChatDTO>>> Handle(ChatRepliesQuery request, CancellationToken cancellationToken)
    {
        var ticket = await _unitOfWork.Tickets.GetAllAsync()
            .AsNoTracking()
            .Where(t => t.Id == request.TicketId && !t.IsDeleted)
            .Select(t => new { t.CustomerId, t.AssignedStaffId })
            .FirstOrDefaultAsync(cancellationToken);

        if (ticket is null)
            return Fail(404, "Ticket not found");

        if (!TicketQueryHelper.CanAccessTicket(ticket.CustomerId, ticket.AssignedStaffId, request.ActorUserId, request.ActorRoles))
            return Fail(403, "Forbidden");

        // Parent có thể đã bị soft-delete — vẫn cho xem replies của nó (đánh dấu "đã xoá" ở FE).
        var parentExists = await _unitOfWork.TicketChats.GetAllAsync()
            .AsNoTracking()
            .AnyAsync(c => c.Id == request.ParentChatId && c.TicketId == request.TicketId, cancellationToken);

        if (!parentExists)
            return Fail(404, "Không tìm thấy bình luận.");

        var canViewInternalChats = TicketQueryHelper.CanViewInternalChats(request.ActorRoles);

        var query = _unitOfWork.TicketChats.GetAllAsync()
            .AsNoTracking()
            .Where(c => c.ParentChatId == request.ParentChatId && !c.IsDeleted);

        if (!canViewInternalChats)
            query = query.Where(c => !c.IsInternal);

        var total = await query.CountAsync(cancellationToken);
        var rawReplies = await query
            .OrderBy(c => c.CreatedAt)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var chatIds = rawReplies.Select(c => c.Id).ToList();
        var mentionsByChat = await ChatChildDataLoader.LoadMentionsAsync(_unitOfWork, chatIds, cancellationToken);
        var reactionsByChat = await ChatChildDataLoader.LoadReactionsAsync(_unitOfWork, chatIds, cancellationToken);

        var items = rawReplies.Select(c => new TicketChatDTO
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

    private static CommonResponse<PaginationResponse<TicketChatDTO>> Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message
    };
}
