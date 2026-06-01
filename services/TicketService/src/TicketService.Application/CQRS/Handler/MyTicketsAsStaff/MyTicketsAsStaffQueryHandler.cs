using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query;
using TicketService.Application.DTOs.Response;
using TicketService.Application.Helpers;
using TicketService.Application.Interfaces.Repositories;

namespace TicketService.Application.CQRS.Handler.MyTicketsAsStaff;

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
        var rawItems = await query
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return new CommonResponse<PaginationResponse<TicketDTO>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new PaginationResponse<TicketDTO>
            {
                Items = rawItems.Select(TicketQueryHelper.MapToTicketDTO).ToList(),
                TotalItems = total,
                PageNumber = request.PageNumber,
                PageSize = request.PageSize
            }
        };
    }
}
