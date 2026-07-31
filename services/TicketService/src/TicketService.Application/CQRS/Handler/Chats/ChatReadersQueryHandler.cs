using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketService.Application.Common.Utils;
using TicketService.Application.CQRS.Query.Chats;
using TicketService.Application.DTOs.Response.Chats;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Chats;

public class ChatReadersQueryHandler : IRequestHandler<ChatReadersQuery, ChatReadersResponse>
{
    private readonly ITicketUnitOfWork _uow;

    public ChatReadersQueryHandler(ITicketUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<ChatReadersResponse> Handle(ChatReadersQuery request, CancellationToken ct)
    {
        var ticket = await _uow.Tickets.GetAllAsync()
            .AsNoTracking()
            .Where(t => t.Id == request.TicketId && !t.IsDeleted)
            .Select(t => new { t.CustomerId, PrimaryHandlerStaffId = t.Assignments.Where(a => !a.IsDeleted && a.Role == AssignmentRoleEnum.PrimaryHandler).Select(a => (Guid?)a.StaffId).FirstOrDefault() })
            .FirstOrDefaultAsync(ct);
        if (ticket == null)
            return new ChatReadersResponse { IsSuccess = false, StatusCode = 404, Message = "Không tìm thấy Ticket." };

        if (!TicketQueryHelper.CanAccessTicket(ticket.CustomerId, ticket.PrimaryHandlerStaffId, request.ActorUserId, request.ActorRoles))
            return new ChatReadersResponse { IsSuccess = false, StatusCode = 403, Message = "Không có quyền truy cập ticket." };

        var chat = await _uow.TicketChats.GetByIdAsync(request.ChatId);
        if (chat == null || chat.IsDeleted || chat.TicketId != request.TicketId
            || (chat.IsInternal && !TicketQueryHelper.CanViewInternalChats(request.ActorRoles)))
        {
            return new ChatReadersResponse { IsSuccess = false, StatusCode = 404, Message = "Không tìm thấy bình luận." };
        }

        var readers = await _uow.TicketChatReads.GetAllAsync()
            .AsNoTracking()
            .Where(r => r.ChatId == request.ChatId && !r.IsDeleted)
            .OrderBy(r => r.ReadAt)
            .Select(r => new ChatReaderDTO
            {
                ChatId = r.ChatId.ToString(),
                UserId = r.UserId.ToString(),
                Role = r.UserRole,
                ReadAt = r.ReadAt
            })
            .ToListAsync(ct);

        return new ChatReadersResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = readers
        };
    }
}
