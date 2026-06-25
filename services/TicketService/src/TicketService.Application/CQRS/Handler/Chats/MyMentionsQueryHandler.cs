using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.MyMentions;
using TicketService.Application.DTOs.Response.Chats;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;

namespace TicketService.Application.CQRS.Handler.Chats;

public class
    MyMentionsQueryHandler : IRequestHandler<MyMentionsQuery, MyMentionsResponse>
{
    private readonly ITicketUnitOfWork _uow;

    public MyMentionsQueryHandler(ITicketUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<MyMentionsResponse> Handle(MyMentionsQuery request, CancellationToken ct)
    {
        var query = _uow.TicketChatMentions.GetAllAsync()
            .AsNoTracking()
            .Include(m => m.Chat)
            .Where(m => m.MentionedUserId == request.ActorUserId && !m.IsDeleted);

        if (request.UnreadOnly)
            query = query.Where(m => !m.IsAcknowledged);

        var total = await query.CountAsync(ct);
        var rawMentions = await query
            .OrderByDescending(m => m.CreatedAt)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(ct);

        var items = rawMentions.Select(m => new TicketChatMentionDTO
        {
            Id = m.Id.ToString(),
            ChatId = m.ChatId.ToString(),
            TicketId = m.Chat.TicketId.ToString(),
            MentionedUserId = m.MentionedUserId.ToString(),
            MentionedUserRole = m.MentionedUserRole,
            MentionedDisplayName = m.MentionedDisplayName,
            IsAcknowledged = m.IsAcknowledged,
            AcknowledgedAt = m.AcknowledgedAt,
            CreatedAt = m.CreatedAt
        }).ToList();

        return new MyMentionsResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new PaginationResponse<TicketChatMentionDTO>
            {
                Items = items,
                TotalItems = total,
                PageNumber = request.PageNumber,
                PageSize = request.PageSize
            }
        };
    }
}
