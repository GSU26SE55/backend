using BatteryService.Application.CQRS.Query.Site;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Mapping;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.Site;

public class GetSiteAssetsQueryHandler : IRequestHandler<GetSiteAssetsQuery, CommonResponse<PaginationResponse<BatteryAssetDto>>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public GetSiteAssetsQueryHandler(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<PaginationResponse<BatteryAssetDto>>> Handle(GetSiteAssetsQuery request, CancellationToken cancellationToken)
    {
        var siteExists = await _unitOfWork.Sites
            .GetAllAsync()
            .AnyAsync(site => site.Id == request.SiteId && !site.IsDeleted, cancellationToken);

        if (!siteExists)
        {
            return new CommonResponse<PaginationResponse<BatteryAssetDto>>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy site."
            };
        }

        var query = _unitOfWork.BatteryAssets
            .GetAllAsync()
            .AsNoTracking()
            .Include(asset => asset.BatteryType)
            .Include(asset => asset.Site)
            .Include(asset => asset.BatteryGroup)
            .Where(asset => asset.SiteId == request.SiteId && !asset.IsDeleted);

        if (request.BatteryGroupId.HasValue)
            query = query.Where(asset => asset.BatteryGroupId == request.BatteryGroupId.Value);

        if (request.Status.HasValue)
            query = query.Where(asset => asset.Status == request.Status.Value);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(asset => asset.CreatedAt)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(asset => BatteryMapper.ToDto(asset))
            .ToListAsync(cancellationToken);

        return new CommonResponse<PaginationResponse<BatteryAssetDto>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new PaginationResponse<BatteryAssetDto>
            {
                Items = items,
                TotalItems = total,
                PageNumber = request.PageNumber,
                PageSize = request.PageSize
            }
        };
    }
}
