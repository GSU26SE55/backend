using BatteryService.Application.CQRS.Query.Site;
using BatteryService.Application.DTOs;
using BatteryService.Application.Helpers;
using BatteryService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.Site;

public class GetSiteByIdQueryHandler : IRequestHandler<GetSiteByIdQuery, CommonResponse<SiteDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IBatteryCurrentUserService _currentUserService;

    public GetSiteByIdQueryHandler(IBatteryUnitOfWork unitOfWork, IBatteryCurrentUserService currentUserService)
    {
        _unitOfWork = unitOfWork;
        _currentUserService = currentUserService;
    }

    public async Task<CommonResponse<SiteDto>> Handle(GetSiteByIdQuery request, CancellationToken cancellationToken)
    {
        // GH-722 — Customer chỉ xem được site của chính mình.
        var scope = BatteryTenantScopeHelper.Resolve(_currentUserService.UserId, _currentUserService.Roles);
        if (scope.IsDenied)
        {
            return new CommonResponse<SiteDto>
            {
                IsSuccess = false,
                StatusCode = 401,
                Message = "Could not identify the current user."
            };
        }

        var customerAccounts = _unitOfWork.CustomerAccounts
            .GetAllAsync()
            .AsNoTracking()
            .Where(account => !account.IsDeleted);

        var siteQuery = _unitOfWork.Sites
            .GetAllAsync()
            .AsNoTracking()
            .Where(site => site.Id == request.Id && !site.IsDeleted);

        // 404 thay vì 403: không tiết lộ rằng site của tenant khác có tồn tại.
        if (scope.IsCustomerScoped)
        {
            siteQuery = siteQuery.Where(site => site.CustomerId == scope.CustomerId);
        }

        var dto = await (
            from site in siteQuery
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
                InstallDate = site.InstallDate,
                Status = site.Status,
                ContactPersonName = site.ContactPersonName,
                ContactPersonPhone = site.ContactPersonPhone,
                BatteryAssetCount = site.BatteryAssets.Count(asset => !asset.IsDeleted),
                ActiveBatteryAssetCount = site.BatteryAssets.Count(asset => !asset.IsDeleted && asset.Status == Domain.Enums.BatteryStatusEnum.Active),
                CreatedAt = site.CreatedAt
            }).FirstOrDefaultAsync(cancellationToken);

        if (dto is null)
        {
            return new CommonResponse<SiteDto>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Site not found."
            };
        }

        return new CommonResponse<SiteDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = dto
        };
    }
}
