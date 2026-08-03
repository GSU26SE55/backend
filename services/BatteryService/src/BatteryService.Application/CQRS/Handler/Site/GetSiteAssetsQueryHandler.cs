using BatteryService.Application.CQRS.Query.Site;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Mapping;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Requests;
using SharedContracts.Common.Responses;
using SharedInfrastructure.Extensions;

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
            .Where(asset => asset.SiteId == request.SiteId && !asset.IsDeleted);

        if (request.Status.HasValue)
            query = query.Where(asset => asset.Status == request.Status.Value);

        var customerAccounts = _unitOfWork.CustomerAccounts
            .GetAllAsync()
            .AsNoTracking()
            .Where(account => !account.IsDeleted);

        // Join account TRƯỚC khi cắt trang (trước đây join sau `pageQuery`): ToPagedEntityListAsync nhận
        // vào IQueryable ĐÃ chiếu sang DTO rồi mới Skip/Take. Đây là LEFT JOIN theo khoá chính của
        // account (1:1) nên số dòng — và do đó totalItems — không đổi. Sắp xếp vẫn dựa trên đúng các
        // cột của asset như cũ nên thứ tự trang giữ nguyên. Cùng khuôn với GetSitesQueryHandler.
        var joined = from asset in query
                     join account in customerAccounts on asset.CustomerId equals account.Id into accountJoin
                     from account in accountJoin.DefaultIfEmpty()
                     select new { asset, account };

        var descending = SortHelper.IsDescending(request.SortDir);
        // Whitelist: serialNumber | batteryTypeName | status | installDate | lastSensorReadingAt | createdAt (default).
        var ordered = (request.SortBy?.Trim().ToLowerInvariant()) switch
        {
            "serialnumber" => descending ? joined.OrderByDescending(x => x.asset.SerialNumber) : joined.OrderBy(x => x.asset.SerialNumber),
            "batterytypename" => descending ? joined.OrderByDescending(x => x.asset.BatteryType.Name) : joined.OrderBy(x => x.asset.BatteryType.Name),
            "status" => descending ? joined.OrderByDescending(x => x.asset.Status) : joined.OrderBy(x => x.asset.Status),
            "installdate" => descending ? joined.OrderByDescending(x => x.asset.InstallDate) : joined.OrderBy(x => x.asset.InstallDate),
            "lastsensorreadingat" => descending ? joined.OrderByDescending(x => x.asset.LastSensorReadingAt) : joined.OrderBy(x => x.asset.LastSensorReadingAt),
            _ => descending ? joined.OrderByDescending(x => x.asset.CreatedAt) : joined.OrderBy(x => x.asset.CreatedAt),
        };

        var page = await ordered
            .ThenBy(x => x.asset.Id) // tie-breaker cố định — pagination ổn định
            .Select(x => new BatteryAssetDto
            {
                Id = x.asset.Id.ToString(),
                SerialNumber = x.asset.SerialNumber,
                BatteryTypeId = x.asset.BatteryTypeId.ToString(),
                BatteryTypeName = x.asset.BatteryType.Name,
                SiteId = x.asset.SiteId.HasValue ? x.asset.SiteId.Value.ToString() : null,
                SiteName = x.asset.Site != null ? x.asset.Site.Name : null,
                CustomerId = x.asset.CustomerId.ToString(),
                CustomerName = x.account != null ? x.account.FullName : string.Empty,
                InstallDate = x.asset.InstallDate,
                WarrantyEndDate = x.asset.WarrantyEndDate,
                WarrantyStatus = x.asset.WarrantyStatus,
                Location = x.asset.Location,
                Latitude = x.asset.Latitude,
                Longitude = x.asset.Longitude,
                Status = x.asset.Status,
                Notes = x.asset.Notes,
                LastSensorReadingAt = x.asset.LastSensorReadingAt,
                CreatedAt = x.asset.CreatedAt
            })
            .ToPagedEntityListAsync(request.PageNumber, request.PageSize, cancellationToken);

        return new CommonResponse<PaginationResponse<BatteryAssetDto>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = page
        };
    }
}
