using BatteryService.Application.CQRS.Query.BatteryGroup;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.BatteryGroup;

public class GetBatteryGroupsQueryHandler : IRequestHandler<GetBatteryGroupsQuery, CommonResponse<PaginationResponse<BatteryGroupDto>>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public GetBatteryGroupsQueryHandler(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<PaginationResponse<BatteryGroupDto>>> Handle(GetBatteryGroupsQuery request, CancellationToken cancellationToken)
    {
        IQueryable<Domain.Entities.BatteryGroup> query = _unitOfWork.BatteryGroups
            .GetAllAsync()
            .AsNoTracking()
            .Include(group => group.Site)
            .Include(group => group.BatteryType);

        if (!request.IncludeDeleted)
            query = query.Where(group => !group.IsDeleted);

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim().ToLower();
            query = query.Where(group =>
                group.Name.ToLower().Contains(keyword) ||
                group.Site.Name.ToLower().Contains(keyword) ||
                group.BatteryType.Name.ToLower().Contains(keyword));
        }

        if (request.SiteId.HasValue)
            query = query.Where(group => group.SiteId == request.SiteId.Value);

        if (request.BatteryTypeId.HasValue)
            query = query.Where(group => group.BatteryTypeId == request.BatteryTypeId.Value);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(group => group.CreatedAt)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(group => new BatteryGroupDto
            {
                Id = group.Id.ToString(),
                SiteId = group.SiteId.ToString(),
                SiteName = group.Site.Name,
                Name = group.Name,
                BatteryTypeId = group.BatteryTypeId.ToString(),
                BatteryTypeName = group.BatteryType.Name,
                BatteryCount = group.BatteryCount,
                CreatedAt = group.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return new CommonResponse<PaginationResponse<BatteryGroupDto>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new PaginationResponse<BatteryGroupDto>
            {
                Items = items,
                TotalItems = total,
                PageNumber = request.PageNumber,
                PageSize = request.PageSize
            }
        };
    }
}
