using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.Common.Utils;
using TicketService.Application.CQRS.Query.Chats;
using TicketService.Application.DTOs.Response.Chats;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Chats;

public class ChatReactionsQueryHandler : IRequestHandler<ChatReactionsQuery, CommonResponse<TicketChatReactionsAggregateDTO>>
{
    private readonly ITicketUnitOfWork _uow;

    public ChatReactionsQueryHandler(ITicketUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<CommonResponse<TicketChatReactionsAggregateDTO>> Handle(ChatReactionsQuery request, CancellationToken ct)
    {
        var ticket = await _uow.Tickets.GetAllAsync()
            .AsNoTracking()
            .Where(t => t.Id == request.TicketId && !t.IsDeleted)
            .Select(t => new { t.CustomerId, PrimaryHandlerStaffId = t.Assignments.Where(a => !a.IsDeleted && a.Role == AssignmentRoleEnum.PrimaryHandler).Select(a => (Guid?)a.StaffId).FirstOrDefault() })
            .FirstOrDefaultAsync(ct);
        if (ticket == null)
        {
            return new CommonResponse<TicketChatReactionsAggregateDTO>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy Ticket."
            };
        }

        if (!TicketQueryHelper.CanAccessTicket(ticket.CustomerId, ticket.PrimaryHandlerStaffId, request.ActorUserId, request.ActorRoles))
        {
            return new CommonResponse<TicketChatReactionsAggregateDTO>
            {
                IsSuccess = false,
                StatusCode = 403,
                Message = "Không có quyền truy cập ticket."
            };
        }

        var chat = await _uow.TicketChats.GetByIdAsync(request.ChatId);
        if (chat == null || chat.IsDeleted || chat.TicketId != request.TicketId
            || (chat.IsInternal && !TicketQueryHelper.CanViewInternalChats(request.ActorRoles)))
        {
            return new CommonResponse<TicketChatReactionsAggregateDTO>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy bình luận."
            };
        }

        var reactions = await _uow.TicketChatReactions.GetAllAsync()
            .AsNoTracking()
            .Where(r => r.ChatId == request.ChatId && !r.IsDeleted)
            .ToListAsync(ct);

        return new CommonResponse<TicketChatReactionsAggregateDTO>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = ChatReactionAggregateHelper.Build(reactions)
        };
    }
}
