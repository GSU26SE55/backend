using BatteryService.Application.CQRS.Query.Site;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.Site;

public class GetSiteByIdQueryHandler : IRequestHandler<GetSiteByIdQuery, CommonResponse<SiteDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public GetSiteByIdQueryHandler(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<SiteDto>> Handle(GetSiteByIdQuery request, CancellationToken cancellationToken)
    {
        var customerAccounts = _unitOfWork.CustomerAccounts
            .GetAllAsync()
            .AsNoTracking()
            .Where(account => !account.IsDeleted);

        var siteQuery = _unitOfWork.Sites
            .GetAllAsync()
            .AsNoTracking()
            .Where(site => site.Id == request.Id && !site.IsDeleted);

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
                CapacityKw = site.CapacityKw,
                InstallDate = site.InstallDate,
                Status = site.Status,
                ContactPersonName = site.ContactPersonName,
                ContactPersonPhone = site.ContactPersonPhone,
                BatteryGroupCount = site.BatteryGroups.Count(batteryGroup => !batteryGroup.IsDeleted),
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
                Message = "Không tìm thấy site."
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
