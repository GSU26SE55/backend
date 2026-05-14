using BatteryService.Application.CQRS.Query.BatteryAsset;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.BatteryAsset;

public class GetBatteryAssetsQueryHandler : IRequestHandler<GetBatteryAssetsQuery, CommonResponse<PaginationResponse<BatteryAssetDto>>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public GetBatteryAssetsQueryHandler(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<PaginationResponse<BatteryAssetDto>>> Handle(GetBatteryAssetsQuery request, CancellationToken cancellationToken)
    {
        IQueryable<Domain.Entities.BatteryAsset> query = _unitOfWork.BatteryAssets
            .GetAllAsync()
            .AsNoTracking()
            .Include(asset => asset.BatteryType)
            .Include(asset => asset.Site)
            .Include(asset => asset.BatteryGroup);

        if (!request.IncludeDeleted)
            query = query.Where(asset => !asset.IsDeleted);

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim().ToLower();
            query = query.Where(asset =>
                asset.SerialNumber.ToLower().Contains(keyword) ||
                (asset.Location != null && asset.Location.ToLower().Contains(keyword)));
        }

        if (request.CustomerId.HasValue)
            query = query.Where(asset => asset.CustomerId == request.CustomerId.Value);

        if (request.BatteryTypeId.HasValue)
            query = query.Where(asset => asset.BatteryTypeId == request.BatteryTypeId.Value);

        if (request.SiteId.HasValue)
            query = query.Where(asset => asset.SiteId == request.SiteId.Value);

        if (request.BatteryGroupId.HasValue)
            query = query.Where(asset => asset.BatteryGroupId == request.BatteryGroupId.Value);

        if (request.Status.HasValue)
            query = query.Where(asset => asset.Status == request.Status.Value);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(asset => asset.CreatedAt)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(asset => new BatteryAssetDto
            {
                Id = asset.Id,
                SerialNumber = asset.SerialNumber,
                BatteryTypeId = asset.BatteryTypeId,
                BatteryTypeName = asset.BatteryType.Name,
                SiteId = asset.SiteId,
                SiteName = asset.Site != null ? asset.Site.Name : null,
                BatteryGroupId = asset.BatteryGroupId,
                BatteryGroupName = asset.BatteryGroup != null ? asset.BatteryGroup.Name : null,
                CustomerId = asset.CustomerId,
                InstallDate = asset.InstallDate,
                WarrantyEndDate = asset.WarrantyEndDate,
                WarrantyStatus = asset.WarrantyStatus,
                Location = asset.Location,
                Latitude = asset.Latitude,
                Longitude = asset.Longitude,
                Status = asset.Status,
                Notes = asset.Notes,
                LastSensorReadingAt = asset.LastSensorReadingAt,
                CreatedAt = asset.CreatedAt
            })
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
