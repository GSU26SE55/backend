using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketService.Application.Common.Utils;
using TicketService.Application.CQRS.Query.Chats;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Chats;

public class MyUnreadCountQueryHandler : IRequestHandler<MyUnreadCountQuery, TicketUnreadCountResponse>
{
    private readonly ITicketUnitOfWork _uow;

    public MyUnreadCountQueryHandler(ITicketUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<TicketUnreadCountResponse> Handle(MyUnreadCountQuery request, CancellationToken ct)
    {
        var actorUserId = request.ActorUserId;
        var actorRoles = request.ActorRoles;

        // Step 1: determine ticket IDs the actor has access to
        bool isManagerOrAdmin = actorRoles.Contains("Admin") || actorRoles.Contains("Manager");
        IQueryable<Guid> ticketIdsQuery;

        if (isManagerOrAdmin)
        {
            ticketIdsQuery = _uow.Tickets.GetAllAsync().AsNoTracking()
                .Where(t => !t.IsDeleted)
                .Select(t => t.Id);
        }
        else
        {
            bool isCustomer = actorRoles.Contains("Customer");
            IQueryable<Guid> directIds;

            if (isCustomer)
            {
                directIds = _uow.Tickets.GetAllAsync().AsNoTracking()
                    .Where(t => !t.IsDeleted && t.CustomerId == actorUserId)
                    .Select(t => t.Id);
            }
            else
            {
                directIds = _uow.TicketAssignments.GetAllAsync().AsNoTracking()
                    .Where(a => a.StaffId == actorUserId && a.Role == AssignmentRoleEnum.PrimaryHandler && !a.IsDeleted)
                    .Select(a => a.TicketId);
            }

            var participantIds = _uow.TicketParticipants.GetAllAsync().AsNoTracking()
                .Where(p => p.UserId == actorUserId && p.RemovedAt == null && !p.IsDeleted)
                .Select(p => p.TicketId);

            ticketIdsQuery = directIds.Union(participantIds);
        }

        // Step 2: subquery of read chat IDs
        var readChatIds = _uow.TicketChatReads.GetAllAsync().AsNoTracking()
            .Where(r => r.UserId == actorUserId && !r.IsDeleted)
            .Select(r => r.ChatId);

        // Step 3: count unread chats (not authored by actor, not already read)
        bool canViewInternal = TicketQueryHelper.CanViewInternalChats(actorRoles);
        var chatsBase = _uow.TicketChats.GetAllAsync().AsNoTracking()
            .Where(c => ticketIdsQuery.Contains(c.TicketId) && !c.IsDeleted && c.AuthorUserId != actorUserId);

        if (!canViewInternal)
            chatsBase = chatsBase.Where(c => !c.IsInternal);

        var count = await chatsBase
            .Where(c => !readChatIds.Contains(c.Id))
            .CountAsync(ct);

        return new TicketUnreadCountResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = count
        };
    }
}
