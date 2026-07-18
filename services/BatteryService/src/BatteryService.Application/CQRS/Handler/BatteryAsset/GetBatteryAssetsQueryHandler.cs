using BatteryService.Application.CQRS.Query.BatteryAsset;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Requests;
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
            .Include(asset => asset.Site);

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

        if (request.Status.HasValue)
            query = query.Where(asset => asset.Status == request.Status.Value);

        var customerAccounts = _unitOfWork.CustomerAccounts
            .GetAllAsync()
            .AsNoTracking()
            .Where(account => !account.IsDeleted);

        var total = await query.CountAsync(cancellationToken);

        // Join account TRƯỚC sort/paginate để sort được theo customerName (join 1:1 nên total không đổi).
        var joined = from asset in query
                     join account in customerAccounts on asset.CustomerId equals account.Id into accountJoin
                     from account in accountJoin.DefaultIfEmpty()
                     select new { asset, account };

        var descending = SortHelper.IsDescending(request.SortDir);
        // Whitelist: serialNumber | batteryTypeName | customerName | siteName | status | installDate | createdAt (default).
        var ordered = (request.SortBy?.Trim().ToLowerInvariant()) switch
        {
            "serialnumber" => descending ? joined.OrderByDescending(x => x.asset.SerialNumber) : joined.OrderBy(x => x.asset.SerialNumber),
            "batterytypename" => descending ? joined.OrderByDescending(x => x.asset.BatteryType.Name) : joined.OrderBy(x => x.asset.BatteryType.Name),
            "customername" => descending ? joined.OrderByDescending(x => x.account != null ? x.account.FullName : null) : joined.OrderBy(x => x.account != null ? x.account.FullName : null),
            "sitename" => descending ? joined.OrderByDescending(x => x.asset.Site != null ? x.asset.Site.Name : null) : joined.OrderBy(x => x.asset.Site != null ? x.asset.Site.Name : null),
            "status" => descending ? joined.OrderByDescending(x => x.asset.Status) : joined.OrderBy(x => x.asset.Status),
            "installdate" => descending ? joined.OrderByDescending(x => x.asset.InstallDate) : joined.OrderBy(x => x.asset.InstallDate),
            _ => descending ? joined.OrderByDescending(x => x.asset.CreatedAt) : joined.OrderBy(x => x.asset.CreatedAt),
        };

        var items = await ordered
            .ThenBy(x => x.asset.Id) // tie-breaker cố định — pagination ổn định
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
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
