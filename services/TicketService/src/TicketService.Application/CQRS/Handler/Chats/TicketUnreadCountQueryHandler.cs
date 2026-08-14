using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketService.Application.Common.Utils;
using TicketService.Application.CQRS.Query.Chats;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Enums;

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
            .Select(t => new { t.CustomerId, PrimaryHandlerStaffId = t.Assignments.Where(a => !a.IsDeleted && a.Role == AssignmentRoleEnum.PrimaryHandler).Select(a => (Guid?)a.StaffId).FirstOrDefault() ?? t.PrimaryHandlerStaffId })
            .FirstOrDefaultAsync(ct);

        if (ticket == null)
            return new TicketUnreadCountResponse { IsSuccess = false, StatusCode = 404, Message = "Ticket not found." };

        var activeParticipants = await _uow.TicketParticipants.GetAllAsync()
            .AsNoTracking()
            .Where(p => p.TicketId == request.TicketId && p.RemovedAt == null && !p.IsDeleted)
            .Select(p => new { p.UserId, p.CanViewInternal })
            .ToListAsync(ct);

        if (!TicketQueryHelper.CanAccessTicket(ticket.CustomerId, ticket.PrimaryHandlerStaffId, request.ActorUserId, request.ActorRoles, activeParticipants.Select(p => p.UserId).ToList()))
            return new TicketUnreadCountResponse { IsSuccess = false, StatusCode = 403, Message = "You do not have permission to access this ticket." };

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
