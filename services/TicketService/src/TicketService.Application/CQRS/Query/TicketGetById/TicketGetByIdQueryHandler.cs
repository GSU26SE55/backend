using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Query.TicketGetById;

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
            .Include(t => t.Comments.Where(c => !c.IsDeleted).OrderByDescending(c => c.CreatedAt))
            .Include(t => t.MaintenanceLogs.Where(m => !m.IsDeleted).OrderByDescending(m => m.CreatedAt))
            .FirstOrDefaultAsync(t => t.Id == request.Id && !t.IsDeleted, cancellationToken);

        if (ticket is null)
            return new CommonResponse<TicketDetailDTO> { IsSuccess = false, StatusCode = 404, Message = "Not found" };

        if (!CanReadTicket(ticket.CustomerId, ticket.AssignedStaffId, request))
            return new CommonResponse<TicketDetailDTO> { IsSuccess = false, StatusCode = 403, Message = "Forbidden" };

        var canViewInternalComments = CanViewInternalComments(request);

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
            RejectionReason = ticket.RejectionReason,
            ClosedAt = ticket.ClosedAt,
            Rating = ticket.Rating,
            RatingComment = ticket.RatingComment,
            RatedAt = ticket.RatedAt,
            EscalatedAt = ticket.EscalatedAt,
            EscalationReason = ticket.EscalationReason,
            CreatedAt = ticket.CreatedAt,
            UpdatedAt = ticket.UpdatedAt,
            SlaTimer = ticket.SlaTimer == null ? null : new SlaTimerDTO
            {
                Id = ticket.SlaTimer.Id.ToString(),
                Priority = ticket.SlaTimer.Priority,
                StartedAt = ticket.SlaTimer.StartedAt,
                DueAt = ticket.SlaTimer.DueAt,
                OriginalDueAt = ticket.SlaTimer.OriginalDueAt,
                TotalPausedMinutes = ticket.SlaTimer.TotalPausedMinutes,
                WarningSentAt = ticket.SlaTimer.WarningSentAt,
                BreachAt = ticket.SlaTimer.BreachAt,
                Status = ticket.SlaTimer.Status,
                RemainingPercent = ticket.SlaTimer.Status == SlaTimerStatusEnum.Running
                    ? (ticket.SlaTimer.DueAt != ticket.SlaTimer.StartedAt
                        ? Math.Max(0, (ticket.SlaTimer.DueAt - DateTime.UtcNow).TotalMinutes /
                            (ticket.SlaTimer.DueAt - ticket.SlaTimer.StartedAt).TotalMinutes * 100)
                        : 0d)
                    : 0
            },
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
            Comments = ticket.Comments
                .Where(c => canViewInternalComments || !c.IsInternal)
                .Select(c => new TicketCommentDTO
                {
                    Id = c.Id.ToString(),
                    TicketId = c.TicketId.ToString(),
                    AuthorUserId = c.AuthorUserId.ToString(),
                    AuthorRole = c.AuthorRole,
                    AuthorDisplayName = c.AuthorDisplayName,
                    Body = c.Body,
                    IsInternal = c.IsInternal,
                    CreatedAt = c.CreatedAt
                }).ToList(),
            MaintenanceLogs = ticket.MaintenanceLogs.Select(m => new MaintenanceLogDTO
            {
                Id = m.Id.ToString(),
                TicketId = m.TicketId.ToString(),
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
            }).ToList()
        };

        return new CommonResponse<TicketDetailDTO> { IsSuccess = true, StatusCode = 200, Data = dto };
    }

    private static bool CanReadTicket(Guid customerId, Guid? assignedStaffId, TicketGetByIdQuery request)
    {
        if (HasAnyRole(request, "Admin", "Manager"))
            return true;

        if (!request.ActorUserId.HasValue)
            return false;

        if (HasRole(request, "Customer") && customerId == request.ActorUserId.Value)
            return true;

        return HasRole(request, "Staff") && assignedStaffId == request.ActorUserId.Value;
    }

    private static bool CanViewInternalComments(TicketGetByIdQuery request)
        => HasAnyRole(request, "Admin", "Manager", "Staff");

    private static bool HasAnyRole(TicketGetByIdQuery request, params string[] roles)
        => roles.Any(role => HasRole(request, role));

    private static bool HasRole(TicketGetByIdQuery request, string role)
        => request.ActorRoles.Any(actorRole => string.Equals(actorRole, role, StringComparison.OrdinalIgnoreCase));
}
