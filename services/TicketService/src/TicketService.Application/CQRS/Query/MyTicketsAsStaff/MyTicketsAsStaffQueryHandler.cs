using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Query.MyTicketsAsStaff;

public class MyTicketsAsStaffQueryHandler : IRequestHandler<MyTicketsAsStaffQuery, CommonResponse<PaginationResponse<TicketDTO>>>
{
    private readonly ITicketUnitOfWork _unitOfWork;

    public MyTicketsAsStaffQueryHandler(ITicketUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<PaginationResponse<TicketDTO>>> Handle(MyTicketsAsStaffQuery request, CancellationToken cancellationToken)
    {
        var query = _unitOfWork.Tickets.GetAllAsync()
            .AsNoTracking()
            .Include(t => t.SlaTimer)
            .Where(t => !t.IsDeleted && t.AssignedStaffId == request.ActorStaffId);

        if (request.Status.HasValue)
            query = query.Where(t => t.Status == request.Status.Value);

        // Sort by SLA urgency: P1 first, then by remaining time
        query = query.OrderBy(t => t.Priority).ThenByDescending(t => t.CreatedAt);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(t => new TicketDTO
            {
                Id = t.Id.ToString(),
                Code = t.Code,
                BatteryAssetId = t.BatteryAssetId.ToString(),
                CustomerId = t.CustomerId.ToString(),
                AssignedStaffId = t.AssignedStaffId.HasValue ? t.AssignedStaffId.Value.ToString() : null,
                Title = t.Title,
                Category = t.Category,
                Priority = t.Priority,
                ImpactScope = t.ImpactScope,
                UrgencyLevel = t.UrgencyLevel,
                Status = t.Status,
                Origin = t.Origin,
                ReopenCount = t.ReopenCount,
                IsIncident = t.IsIncident,
                CreatedAt = t.CreatedAt,
                UpdatedAt = t.UpdatedAt,
                SlaTimer = t.SlaTimer == null ? null : new SlaTimerDTO
                {
                    Id = t.SlaTimer.Id.ToString(),
                    Priority = t.SlaTimer.Priority,
                    StartedAt = t.SlaTimer.StartedAt,
                    DueAt = t.SlaTimer.DueAt,
                    OriginalDueAt = t.SlaTimer.OriginalDueAt,
                    TotalPausedMinutes = t.SlaTimer.TotalPausedMinutes,
                    WarningSentAt = t.SlaTimer.WarningSentAt,
                    BreachAt = t.SlaTimer.BreachAt,
                    Status = t.SlaTimer.Status,
                    RemainingPercent = t.SlaTimer.Status == SlaTimerStatusEnum.Running
                        ? (t.SlaTimer.DueAt != t.SlaTimer.StartedAt
                            ? Math.Max(0, (t.SlaTimer.DueAt - DateTime.UtcNow).TotalMinutes /
                                (t.SlaTimer.DueAt - t.SlaTimer.StartedAt).TotalMinutes * 100)
                            : 0d)
                        : 0d
                }
            })
            .ToListAsync(cancellationToken);

        return new CommonResponse<PaginationResponse<TicketDTO>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new PaginationResponse<TicketDTO>
            {
                Items = items,
                TotalItems = total,
                PageNumber = request.PageNumber,
                PageSize = request.PageSize
            }
        };
    }
}
