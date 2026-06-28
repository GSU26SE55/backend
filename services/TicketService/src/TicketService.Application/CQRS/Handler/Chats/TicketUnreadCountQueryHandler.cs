using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketService.Application.CQRS.Query.Chats;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Helpers;
using TicketService.Application.Interfaces.Repositories;

namespace TicketService.Application.CQRS.Handler.Chats;

public class TicketUnreadCountQueryHandler : IRequestHandler<TicketUnreadCountQuery, TicketUnreadCountResponse>
{
    private readonly ITicketUnitOfWork _uow;

    public TicketUnreadCountQueryHandler(ITicketUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<TicketUnreadCountResponse> Handle(TicketUnreadCountQuery request, CancellationToken ct)
    {
        var ticket = await _uow.Tickets.GetAllAsync()
            .AsNoTracking()
            .Where(t => t.Id == request.TicketId && !t.IsDeleted)
            .Select(t => new { t.CustomerId, t.AssignedStaffId })
            .FirstOrDefaultAsync(ct);

        if (ticket == null)
            return new TicketUnreadCountResponse { IsSuccess = false, StatusCode = 404, Message = "Không tìm thấy ticket." };

        var activeParticipants = await _uow.TicketParticipants.GetAllAsync()
            .AsNoTracking()
            .Where(p => p.TicketId == request.TicketId && p.RemovedAt == null && !p.IsDeleted)
            .Select(p => new { p.UserId, p.CanViewInternal })
            .ToListAsync(ct);

        if (!TicketQueryHelper.CanAccessTicket(ticket.CustomerId, ticket.AssignedStaffId, request.ActorUserId, request.ActorRoles, activeParticipants.Select(p => p.UserId).ToList()))
            return new TicketUnreadCountResponse { IsSuccess = false, StatusCode = 403, Message = "Không có quyền truy cập ticket." };

        var participantCanViewInternal = activeParticipants.Any(p => p.UserId == request.ActorUserId && p.CanViewInternal);
        var canViewInternalChats = TicketQueryHelper.CanViewInternalChats(request.ActorRoles, participantCanViewInternal);

        var chatsQuery = _uow.TicketChats.GetAllAsync()
            .AsNoTracking()
            .Where(c => c.TicketId == request.TicketId && !c.IsDeleted && c.AuthorUserId != request.ActorUserId);

        if (!canViewInternalChats)
            chatsQuery = chatsQuery.Where(c => !c.IsInternal);

        var readChatIds = _uow.TicketChatReads.GetAllAsync()
            .AsNoTracking()
            .Where(r => r.UserId == request.ActorUserId && !r.IsDeleted)
            .Select(r => r.ChatId);

        var unreadCount = await chatsQuery
            .Where(c => !readChatIds.Contains(c.Id))
            .CountAsync(ct);

        return new TicketUnreadCountResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = unreadCount
        };
    }
}
