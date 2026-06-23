using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketService.Application.CQRS.Query.ParticipantHistory;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Helpers;
using TicketService.Application.Interfaces.Repositories;

namespace TicketService.Application.CQRS.Handler.Participants;

public class ParticipantHistoryQueryHandler : IRequestHandler<ParticipantHistoryQuery, ParticipantHistoryResponse>
{
    private readonly ITicketUnitOfWork _unitOfWork;

    public ParticipantHistoryQueryHandler(ITicketUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<ParticipantHistoryResponse> Handle(ParticipantHistoryQuery request, CancellationToken cancellationToken)
    {
        var ticketExists = await _unitOfWork.Tickets.GetAllAsync()
            .AsNoTracking()
            .AnyAsync(t => t.Id == request.TicketId && !t.IsDeleted, cancellationToken);

        if (!ticketExists)
            return Fail(404, "Không tìm thấy Ticket.");

        if (!TicketQueryHelper.CanViewInternalChats(request.ActorRoles))
            return Fail(403, "Chỉ Staff/Manager/Admin mới có quyền xem lịch sử participant.");

        var history = await _unitOfWork.TicketParticipants.GetAllAsync()
            .AsNoTracking()
            .Where(p => p.TicketId == request.TicketId && !p.IsDeleted)
            .OrderBy(p => p.AddedAt)
            .Select(p => new ParticipantHistoryDTO
            {
                Id = p.Id.ToString(),
                TicketId = p.TicketId.ToString(),
                UserId = p.UserId.ToString(),
                UserRole = p.UserRole,
                ParticipantType = p.ParticipantType,
                CanPost = p.CanPost,
                CanViewInternal = p.CanViewInternal,
                AddedByUserId = p.AddedByUserId.ToString(),
                AddedAt = p.AddedAt,
                RemovedAt = p.RemovedAt,
                RemovedByUserId = p.RemovedByUserId.HasValue ? p.RemovedByUserId.Value.ToString() : null,
                RemoveReason = p.RemoveReason
            })
            .ToListAsync(cancellationToken);

        return new ParticipantHistoryResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = history
        };
    }

    private static ParticipantHistoryResponse Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message
    };
}
