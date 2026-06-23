using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketService.Application.CQRS.Query.TicketParticipants;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Helpers;
using TicketService.Application.Interfaces.Repositories;

namespace TicketService.Application.CQRS.Handler.Participants;

public class TicketParticipantsQueryHandler : IRequestHandler<TicketParticipantsQuery, TicketParticipantsResponse>
{
    private readonly ITicketUnitOfWork _unitOfWork;

    public TicketParticipantsQueryHandler(ITicketUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<TicketParticipantsResponse> Handle(TicketParticipantsQuery request, CancellationToken cancellationToken)
    {
        var ticket = await _unitOfWork.Tickets.GetAllAsync()
            .AsNoTracking()
            .Where(t => t.Id == request.TicketId && !t.IsDeleted)
            .Select(t => new { t.CustomerId, t.AssignedStaffId })
            .FirstOrDefaultAsync(cancellationToken);

        if (ticket is null)
            return Fail(404, "Không tìm thấy Ticket.");

        var activeParticipants = await _unitOfWork.TicketParticipants.GetAllAsync()
            .AsNoTracking()
            .Where(p => p.TicketId == request.TicketId && p.RemovedAt == null && !p.IsDeleted)
            .ToListAsync(cancellationToken);

        if (!TicketQueryHelper.CanAccessTicket(ticket.CustomerId, ticket.AssignedStaffId, request.ActorUserId, request.ActorRoles, activeParticipants.Select(p => p.UserId).ToList()))
            return Fail(403, "Không có quyền truy cập ticket này.");

        var items = activeParticipants
            .OrderBy(p => p.AddedAt)
            .Select(p => new TicketParticipantDTO
            {
                Id = p.Id.ToString(),
                TicketId = p.TicketId.ToString(),
                UserId = p.UserId.ToString(),
                UserRole = p.UserRole,
                ParticipantType = p.ParticipantType,
                CanPost = p.CanPost,
                CanViewInternal = p.CanViewInternal,
                AddedByUserId = p.AddedByUserId.ToString(),
                AddedAt = p.AddedAt
            })
            .ToList();

        return new TicketParticipantsResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = items
        };
    }

    private static TicketParticipantsResponse Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message
    };
}
