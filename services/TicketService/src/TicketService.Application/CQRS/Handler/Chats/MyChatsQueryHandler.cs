using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.Common.Helpers;
using TicketService.Application.CQRS.Query.MyChats;
using TicketService.Application.DTOs.Response.Chats;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;

namespace TicketService.Application.CQRS.Handler.Chats;

public class MyChatsQueryHandler : IRequestHandler<MyChatsQuery, CommonResponse<PaginationResponse<TicketChatDTO>>>
{
    private readonly ITicketUnitOfWork _unitOfWork;

    public MyChatsQueryHandler(ITicketUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<PaginationResponse<TicketChatDTO>>> Handle(MyChatsQuery request, CancellationToken cancellationToken)
    {
        var query = _unitOfWork.TicketChats.GetAllAsync()
            .AsNoTracking()
            .Where(c => c.AuthorUserId == request.ActorUserId && !c.IsDeleted);

        var total = await query.CountAsync(cancellationToken);
        var rawChats = await query
            .OrderByDescending(c => c.CreatedAt)
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
            EditedAt = c.EditedAt,
            EditCount = c.EditCount,
            LastEditedByUserId = c.LastEditedByUserId?.ToString(),
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
