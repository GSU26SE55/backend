using BatteryService.Application.CQRS.Query.Site;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

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

        var total = await query.CountAsync(cancellationToken);
        var pageQuery = query
            .OrderByDescending(site => site.CreatedAt)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize);

        var items = await (
            from site in pageQuery
            join account in customerAccounts on site.CustomerId equals account.Id into accountJoin
            from account in accountJoin.DefaultIfEmpty()
            select new SiteDto
            {
                Id = site.Id.ToString(),
                Name = site.Name,
                CustomerId = site.CustomerId.ToString(),
                CustomerName = account != null ? account.FullName : string.Empty,
                Address = site.Address,
                Latitude = site.Latitude,
                Longitude = site.Longitude,
                CapacityKw = site.CapacityKw,
                InstallDate = site.InstallDate,
                Status = site.Status,
                ContactPersonName = site.ContactPersonName,
                ContactPersonPhone = site.ContactPersonPhone,
                BatteryGroupCount = site.BatteryGroups.Count(batteryGroup => !batteryGroup.IsDeleted),
                BatteryAssetCount = site.BatteryAssets.Count(asset => !asset.IsDeleted),
                ActiveBatteryAssetCount = site.BatteryAssets.Count(asset => !asset.IsDeleted && asset.Status == Domain.Enums.BatteryStatusEnum.Active),
                CreatedAt = site.CreatedAt
            }).ToListAsync(cancellationToken);

        return new CommonResponse<PaginationResponse<SiteDto>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new PaginationResponse<SiteDto>
            {
                Items = items,
                TotalItems = total,
                PageNumber = request.PageNumber,
                PageSize = request.PageSize
            }
        };
    }

    private IQueryable<Domain.Entities.Site> BuildSiteQuery(bool includeDeleted)
    {
        var query = _unitOfWork.Sites
            .GetAllAsync()
            .AsNoTracking()
            .Include(site => site.BatteryGroups)
            .Include(site => site.BatteryAssets);

        return includeDeleted ? query : query.Where(site => !site.IsDeleted);
    }
}
