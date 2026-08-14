using BatteryService.Application.CQRS.Query.Site;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Requests;
using SharedContracts.Common.Responses;
using SharedInfrastructure.Extensions;

namespace BatteryService.Application.CQRS.Handler.Site;

public class GetSitesQueryHandler : IRequestHandler<GetSitesQuery, CommonResponse<PaginationResponse<SiteDto>>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public GetSitesQueryHandler(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<PaginationResponse<SiteDto>>> Handle(GetSitesQuery request, CancellationToken cancellationToken)
    {
        var query = BuildSiteQuery(request.IncludeDeleted);

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim().ToLower();
            query = query.Where(site =>
                site.Name.ToLower().Contains(keyword) ||
                (site.Address != null && site.Address.ToLower().Contains(keyword)));
        }

        if (request.CustomerId.HasValue)
            query = query.Where(site => site.CustomerId == request.CustomerId.Value);

        if (request.Status.HasValue)
            query = query.Where(site => site.Status == request.Status.Value);

        var customerAccounts = _unitOfWork.CustomerAccounts
            .GetAllAsync()
            .AsNoTracking()
            .Where(account => !account.IsDeleted);

        // Join account TRƯỚC sort/paginate để sort được theo customerName (join 1:1 nên total không đổi).
        var joined = from site in query
                     join account in customerAccounts on site.CustomerId equals account.Id into accountJoin
                     from account in accountJoin.DefaultIfEmpty()
                     select new { site, account };

        var descending = SortHelper.IsDescending(request.SortDir);
        // Whitelist: name | customerName | status | batteryAssetCount | installDate | createdAt (default).
        var ordered = (request.SortBy?.Trim().ToLowerInvariant()) switch
        {
            "name" => descending ? joined.OrderByDescending(x => x.site.Name) : joined.OrderBy(x => x.site.Name),
            "customername" => descending ? joined.OrderByDescending(x => x.account != null ? x.account.FullName : null) : joined.OrderBy(x => x.account != null ? x.account.FullName : null),
            "status" => descending ? joined.OrderByDescending(x => x.site.Status) : joined.OrderBy(x => x.site.Status),
            "batteryassetcount" => descending ? joined.OrderByDescending(x => x.site.BatteryAssets.Count(asset => !asset.IsDeleted)) : joined.OrderBy(x => x.site.BatteryAssets.Count(asset => !asset.IsDeleted)),
            "installdate" => descending ? joined.OrderByDescending(x => x.site.InstallDate) : joined.OrderBy(x => x.site.InstallDate),
            _ => descending ? joined.OrderByDescending(x => x.site.CreatedAt) : joined.OrderBy(x => x.site.CreatedAt),
        };

        var page = await ordered
            .ThenBy(x => x.site.Id) // tie-breaker cố định — pagination ổn định
            .Select(x => new SiteDto
            {
                Id = x.site.Id.ToString(),
                Name = x.site.Name,
                CustomerId = x.site.CustomerId.ToString(),
                CustomerName = x.account != null ? x.account.FullName : string.Empty,
                Address = x.site.Address,
                Latitude = x.site.Latitude,
                Longitude = x.site.Longitude,
                InstallDate = x.site.InstallDate,
                Status = x.site.Status,
                ContactPersonName = x.site.ContactPersonName,
                ContactPersonPhone = x.site.ContactPersonPhone,
                BatteryAssetCount = x.site.BatteryAssets.Count(asset => !asset.IsDeleted),
                ActiveBatteryAssetCount = x.site.BatteryAssets.Count(asset => !asset.IsDeleted && asset.Status == Domain.Enums.BatteryStatusEnum.Active),
                CreatedAt = x.site.CreatedAt
            })
            .ToPagedEntityListAsync(request.PageNumber, request.PageSize, cancellationToken);

        return new CommonResponse<PaginationResponse<SiteDto>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = page
        };
    }

    private IQueryable<Domain.Entities.Site> BuildSiteQuery(bool includeDeleted)
    {
        var query = _unitOfWork.Sites
            .GetAllAsync()
            .AsNoTracking()
            .Include(site => site.BatteryAssets);

        return includeDeleted ? query : query.Where(site => !site.IsDeleted);
    }
}
