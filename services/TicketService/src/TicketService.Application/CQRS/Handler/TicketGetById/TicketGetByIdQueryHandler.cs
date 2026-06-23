using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.Ticket;
using TicketService.Application.DTOs.Response.Maintenances;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Helpers;
using TicketService.Application.Interfaces.Repositories;

namespace TicketService.Application.CQRS.Handler.TicketGetById;

public class TicketGetByIdQueryHandler : IRequestHandler<TicketGetByIdQuery, CommonResponse<TicketDetailDTO>>
{
    private readonly ITicketUnitOfWork _unitOfWork;

    public TicketGetByIdQueryHandler(ITicketUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<TicketDetailDTO>> Handle(TicketGetByIdQuery request, CancellationToken cancellationToken)
    {
        var ticket = await _unitOfWork.Tickets.GetAllAsync()
            .AsNoTracking()
            .Include(t => t.SlaTimer)
            .Include(t => t.Activities.OrderByDescending(a => a.CreatedAt))
            .Include(t => t.Chats.Where(c => !c.IsDeleted).OrderByDescending(c => c.CreatedAt))
            .Include(t => t.MaintenanceLogs.Where(m => !m.IsDeleted).OrderByDescending(m => m.CreatedAt))
            .Include(t => t.Attachments.Where(a => !a.IsDeleted).OrderByDescending(a => a.CreatedAt))
            .FirstOrDefaultAsync(t => t.Id == request.Id && !t.IsDeleted, cancellationToken);

        if (ticket is null)
            return new CommonResponse<TicketDetailDTO> { IsSuccess = false, StatusCode = 404, Message = "Not found" };

        var activeParticipants = await _unitOfWork.TicketParticipants.GetAllAsync()
            .AsNoTracking()
            .Where(p => p.TicketId == request.Id && p.RemovedAt == null && !p.IsDeleted)
            .Select(p => new { p.UserId, p.CanViewInternal })
            .ToListAsync(cancellationToken);

        if (!TicketQueryHelper.CanAccessTicket(ticket.CustomerId, ticket.AssignedStaffId, request.ActorUserId, request.ActorRoles, activeParticipants.Select(p => p.UserId).ToList()))
            return new CommonResponse<TicketDetailDTO> { IsSuccess = false, StatusCode = 403, Message = "Forbidden" };

        var participantCanViewInternal = request.ActorUserId.HasValue
            && activeParticipants.Any(p => p.UserId == request.ActorUserId.Value && p.CanViewInternal);
        var canViewInternalChats = TicketQueryHelper.CanViewInternalChats(request.ActorRoles, participantCanViewInternal);

        var dto = new TicketDetailDTO
        {
            Id = ticket.Id.ToString(),
            Code = ticket.Code,
            BatteryAssetId = ticket.BatteryAssetId.ToString(),
            CustomerId = ticket.CustomerId.ToString(),
            AssignedStaffId = ticket.AssignedStaffId?.ToString(),
            Title = ticket.Title,
            Description = ticket.Description,
            Category = ticket.Category,
            Priority = ticket.Priority,
            ImpactScope = ticket.ImpactScope,
            UrgencyLevel = ticket.UrgencyLevel,
            Status = ticket.Status,
            Origin = ticket.Origin,
            OriginAlertId = ticket.OriginAlertId?.ToString(),
            ReopenCount = ticket.ReopenCount,
            IsIncident = ticket.IsIncident,
            ResolutionSummary = ticket.ResolutionSummary,
            ResolvedAt = ticket.ResolvedAt,
            ResolvedByStaffId = ticket.ResolvedByStaffId?.ToString(),
            ApprovedAt = ticket.ApprovedAt,
            ApprovedByManagerId = ticket.ApprovedByManagerId?.ToString(),
            RejectionReason = ticket.Reason,
            ClosedAt = ticket.ClosedAt,
            Rating = ticket.Rating,
            RatingComment = ticket.RatingComment,
            RatedAt = ticket.RatedAt,
            EscalatedAt = ticket.EscalatedAt,
            EscalationReason = ticket.EscalationReason,
            CreatedAt = ticket.CreatedAt,
            UpdatedAt = ticket.UpdatedAt,
            SlaTimer = TicketQueryHelper.MapToSlaTimerDTO(ticket.SlaTimer),
            Activities = ticket.Activities.Select(a => new TicketActivityDTO
            {
                Id = a.Id.ToString(),
                TicketId = a.TicketId.ToString(),
                ActorUserId = a.ActorUserId?.ToString(),
                ActorRole = a.ActorRole,
                ActorDisplayName = a.ActorDisplayName,
                Action = a.Action,
                OldValue = a.OldValue,
                NewValue = a.NewValue,
                Reason = a.Reason,
                CreatedAt = a.CreatedAt
            }).ToList(),
            Chats = ticket.Chats
                .Where(c => canViewInternalChats || !c.IsInternal)
                .Select(c => new TicketChatDTO
                {
                    Id = c.Id.ToString(),
                    TicketId = c.TicketId.ToString(),
                    AuthorUserId = c.AuthorUserId.ToString(),
                    AuthorRole = c.AuthorRole,
                    AuthorDisplayName = c.AuthorDisplayName,
                    Body = c.Body,
                    IsInternal = c.IsInternal,
                    AttachmentFileIds = (c.AttachmentFileIds ?? new List<Guid>())
                        .Select(fid => fid.ToString())
                        .ToList(),
                    CreatedAt = c.CreatedAt
                }).ToList(),
            MaintenanceLogs = ticket.MaintenanceLogs.Select(m => new MaintenanceLogDTO
            {
                Id = m.Id.ToString(),
                StaffId = m.StaffId.ToString(),
                LogType = m.LogType,
                Summary = m.Summary,
                DiagnosisDetails = m.DiagnosisDetails,
                ActionsTaken = m.ActionsTaken,
                DurationMinutes = m.DurationMinutes,
                ResolutionNote = m.ResolutionNote,
                StartedAt = m.StartedAt,
                CompletedAt = m.CompletedAt,
                CreatedAt = m.CreatedAt
            }).ToList(),
            AttachmentFileIds = ticket.Attachments.Select(a => a.FileId.ToString()).ToList()
        };

        return new CommonResponse<TicketDetailDTO> { IsSuccess = true, StatusCode = 200, Data = dto };
    }
}
