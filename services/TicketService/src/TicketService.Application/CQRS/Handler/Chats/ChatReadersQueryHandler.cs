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
            return new ChatReadersResponse { IsSuccess = false, StatusCode = 404, Message = "Ticket not found." };

        if (!TicketQueryHelper.CanAccessTicket(ticket.CustomerId, ticket.PrimaryHandlerStaffId, request.ActorUserId, request.ActorRoles))
            return new ChatReadersResponse { IsSuccess = false, StatusCode = 403, Message = "You do not have permission to access this ticket." };

        var chat = await _uow.TicketChats.GetByIdAsync(request.ChatId);
        if (chat == null || chat.IsDeleted || chat.TicketId != request.TicketId
            || (chat.IsInternal && !TicketQueryHelper.CanViewInternalChats(request.ActorRoles)))
        {
            return new ChatReadersResponse { IsSuccess = false, StatusCode = 404, Message = "Comment not found." };
        }

        var reads = await _uow.TicketChatReads.GetAllAsync()
            .AsNoTracking()
            .Where(r => r.ChatId == request.ChatId && !r.IsDeleted)
            .OrderBy(r => r.ReadAt)
            .ToListAsync(ct);

        // Resolve tên hiển thị — cùng cách TicketParticipantsQueryHandler làm.
        var readerUserIds = reads.Select(r => r.UserId).Distinct().ToList();
        var customerNames = await _uow.CustomerAccounts.GetAllAsync()
            .AsNoTracking()
            .Where(a => readerUserIds.Contains(a.AccountId) && !a.IsDeleted)
            .ToDictionaryAsync(a => a.AccountId, a => a.FullName, ct);
        var staffNames = await _uow.StaffAccounts.GetAllAsync()
            .AsNoTracking()
            .Where(a => readerUserIds.Contains(a.AccountId) && !a.IsDeleted)
            .ToDictionaryAsync(a => a.AccountId, a => a.FullName, ct);

        var readers = reads
            .Select(r => new ChatReaderDTO
            {
                ChatId = r.ChatId.ToString(),
                UserId = r.UserId.ToString(),
                DisplayName = r.UserRole == ActorRoleEnum.Customer
                    ? customerNames.GetValueOrDefault(r.UserId, r.UserId.ToString())
                    : staffNames.GetValueOrDefault(r.UserId, r.UserId.ToString()),
                Role = r.UserRole,
                ReadAt = r.ReadAt
            })
            .ToList();

        return new ChatReadersResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = readers
        };
    }
}
