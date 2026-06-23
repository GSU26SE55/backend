using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.Ticket;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Helpers;
using TicketService.Application.Interfaces.Repositories;

namespace TicketService.Application.CQRS.Query.TicketActivityTimeline;

public class TicketActivityTimelineQueryHandler : IRequestHandler<TicketActivityTimelineQuery, CommonResponse<List<TicketActivityDTO>>>
{
    private readonly ITicketUnitOfWork _unitOfWork;

    public TicketActivityTimelineQueryHandler(ITicketUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<List<TicketActivityDTO>>> Handle(TicketActivityTimelineQuery request, CancellationToken cancellationToken)
    {
        var ticket = await _unitOfWork.Tickets.GetAllAsync()
            .AsNoTracking()
            .Where(t => t.Id == request.TicketId && !t.IsDeleted)
            .Select(t => new { t.CustomerId, t.AssignedStaffId })
            .FirstOrDefaultAsync(cancellationToken);

        if (ticket is null)
            return new CommonResponse<List<TicketActivityDTO>> { IsSuccess = false, StatusCode = 404, Message = "Not found" };

        var activeParticipantUserIds = await _unitOfWork.TicketParticipants.GetAllAsync()
            .AsNoTracking()
            .Where(p => p.TicketId == request.TicketId && p.RemovedAt == null && !p.IsDeleted)
            .Select(p => p.UserId)
            .ToListAsync(cancellationToken);

        if (!TicketQueryHelper.CanAccessTicket(ticket.CustomerId, ticket.AssignedStaffId, request.ActorUserId, request.ActorRoles, activeParticipantUserIds))
            return new CommonResponse<List<TicketActivityDTO>> { IsSuccess = false, StatusCode = 403, Message = "Forbidden" };

        var activities = await _unitOfWork.TicketActivities.GetAllAsync()
            .AsNoTracking()
            .Where(a => a.TicketId == request.TicketId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new TicketActivityDTO
            {
                Id = a.Id.ToString(),
                TicketId = a.TicketId.ToString(),
                ActorUserId = a.ActorUserId.HasValue ? a.ActorUserId.Value.ToString() : null,
                ActorRole = a.ActorRole,
                ActorDisplayName = a.ActorDisplayName,
                Action = a.Action,
                OldValue = a.OldValue,
                NewValue = a.NewValue,
                Reason = a.Reason,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return new CommonResponse<List<TicketActivityDTO>> { IsSuccess = true, StatusCode = 200, Data = activities };
    }
}
