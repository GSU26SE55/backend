using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedInfrastructure.Extensions;
using TicketService.Application.Common.Utils;
using TicketService.Application.CQRS.Query.Chats;
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
        var canViewInternal = TicketQueryHelper.CanViewInternalChats(request.ActorRoles);
        var activeParticipants = _uow.TicketParticipants.GetAllAsync()
            .AsNoTracking()
            .Where(p => p.UserId == request.ActorUserId && p.RemovedAt == null && !p.IsDeleted);

        var query = _uow.TicketChatMentions.GetAllAsync()
            .AsNoTracking()
            .Include(m => m.Chat)
            .Where(m => m.MentionedUserId == request.ActorUserId
                && !m.IsDeleted
                && !m.Chat.IsDeleted
                && activeParticipants.Any(p => p.TicketId == m.Chat.TicketId)
                && (!m.Chat.IsInternal || canViewInternal));

        // Phân trang trên entity: sau đó còn nạp dữ liệu con (mention/reaction) theo lô rồi mới dựng DTO.
        var page = await query
            .OrderByDescending(m => m.CreatedAt)
            .ThenBy(m => m.Id) // tie-breaker cố định — pagination ổn định
            .ToPagedEntityListAsync(request.PageNumber, request.PageSize, ct);
        var rawMentions = page.Items;

        var items = rawMentions.Select(m => new TicketChatMentionDTO
        {
            Id = m.Id.ToString(),
            ChatId = m.ChatId.ToString(),
            TicketId = m.Chat.TicketId.ToString(),
            MentionedUserId = m.MentionedUserId.ToString(),
            MentionedUserRole = m.MentionedUserRole,
            MentionedDisplayName = m.MentionedDisplayName,
            IsInternal = m.Chat.IsInternal,
            CreatedAt = m.CreatedAt
        }).ToList();

        return new MyMentionsResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = page.WithItems(items)
        };
    }
}
