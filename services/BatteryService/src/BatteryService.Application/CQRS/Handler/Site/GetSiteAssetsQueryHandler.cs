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

        var customerAccounts = _unitOfWork.CustomerAccounts
            .GetAllAsync()
            .AsNoTracking()
            .Where(account => !account.IsDeleted);

        var total = await query.CountAsync(cancellationToken);
        var pageQuery = query
            .OrderByDescending(asset => asset.CreatedAt)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize);

        var items = await (
            from asset in pageQuery
            join account in customerAccounts on asset.CustomerId equals account.Id into accountJoin
            from account in accountJoin.DefaultIfEmpty()
            select new BatteryAssetDto
            {
                Id = asset.Id.ToString(),
                SerialNumber = asset.SerialNumber,
                BatteryTypeId = asset.BatteryTypeId.ToString(),
                BatteryTypeName = asset.BatteryType.Name,
                SiteId = asset.SiteId.HasValue ? asset.SiteId.Value.ToString() : null,
                SiteName = asset.Site != null ? asset.Site.Name : null,
                BatteryGroupId = asset.BatteryGroupId.HasValue ? asset.BatteryGroupId.Value.ToString() : null,
                BatteryGroupName = asset.BatteryGroup != null ? asset.BatteryGroup.Name : null,
                CustomerId = asset.CustomerId.ToString(),
                CustomerName = account != null ? account.FullName : string.Empty,
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
            }).ToListAsync(cancellationToken);

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
