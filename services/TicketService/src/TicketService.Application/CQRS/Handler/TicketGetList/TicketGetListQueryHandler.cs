using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query;
using TicketService.Application.DTOs.Response;
using TicketService.Application.Helpers;
using TicketService.Application.Interfaces.Repositories;

namespace TicketService.Application.CQRS.Handler.TicketGetList;

public class TicketGetListQueryHandler : IRequestHandler<TicketGetListQuery, CommonResponse<PaginationResponse<TicketDTO>>>
{
    private readonly ITicketUnitOfWork _unitOfWork;

    public TicketGetListQueryHandler(ITicketUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<PaginationResponse<TicketDTO>>> Handle(TicketGetListQuery request, CancellationToken cancellationToken)
    {
        var query = _unitOfWork.Tickets.GetAllAsync()
            .AsNoTracking()
            .Include(t => t.SlaTimer)
            .Where(t => !t.IsDeleted);

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var kw = request.Keyword.Trim().ToLower();
            query = query.Where(t => t.Title.ToLower().Contains(kw) || t.Code.ToLower().Contains(kw));
        }

        if (request.Status.HasValue)
            query = query.Where(t => t.Status == request.Status.Value);

        if (request.Priority.HasValue)
            query = query.Where(t => t.Priority == request.Priority.Value);

        if (request.Category.HasValue)
            query = query.Where(t => t.Category == request.Category.Value);

        if (request.BatteryAssetId.HasValue)
            query = query.Where(t => t.BatteryAssetId == request.BatteryAssetId.Value);

        query = request.IsDescending
            ? query.OrderByDescending(t => t.CreatedAt)
            : query.OrderBy(t => t.CreatedAt);

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
