using BatteryService.Application.CQRS.Query.Site;
using BatteryService.Application.DTOs;
using BatteryService.Application.Helpers;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Mapping;
using BatteryService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Requests;
using SharedContracts.Common.Responses;
using SharedInfrastructure.Extensions;

namespace BatteryService.Application.CQRS.Handler.Site;

public class GetSiteAssetsQueryHandler : IRequestHandler<GetSiteAssetsQuery, CommonResponse<PaginationResponse<BatteryAssetDto>>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IBatteryCurrentUserService _currentUserService;

    public GetSiteAssetsQueryHandler(IBatteryUnitOfWork unitOfWork, IBatteryCurrentUserService currentUserService)
    {
        _unitOfWork = unitOfWork;
        _currentUserService = currentUserService;
    }

    public async Task<CommonResponse<PaginationResponse<BatteryAssetDto>>> Handle(GetSiteAssetsQuery request, CancellationToken cancellationToken)
    {
        // GH-722 — Customer chỉ liệt kê được asset của site thuộc mình.
        var scope = BatteryTenantScopeHelper.Resolve(_currentUserService.UserId, _currentUserService.Roles);
        if (scope.IsDenied)
        {
            return new CommonResponse<PaginationResponse<BatteryAssetDto>>
            {
                IsSuccess = false,
                StatusCode = 401,
                Message = "Could not identify the current user."
            };
        }

        // 404 thay vì 403: không tiết lộ rằng site của tenant khác có tồn tại.
        var siteExists = await BatteryTenantAccessGuard.CanAccessSiteAsync(_unitOfWork, request.SiteId, scope, cancellationToken)
            && await _unitOfWork.Sites
                .GetAllAsync()
                .AnyAsync(site => site.Id == request.SiteId && !site.IsDeleted, cancellationToken);

        if (!siteExists)
        {
            return new CommonResponse<PaginationResponse<BatteryAssetDto>>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Site not found."
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

        // Cùng logic AssetsWithActiveAlerts của GetSiteDashboardQueryHandler — con số alert ở đây
        // phải khớp con số "Open alerts" trên Site overview phía trên bảng. Lọc theo assetIds
        // của site (không dùng Alert.SiteId) vì field đó có thể null trên alert cấp asset — chỉ
        // site-level alert (EnvironmentalIncident) mới chắc chắn populate nó.
        var siteAssetIds = await _unitOfWork.BatteryAssets
            .GetAllAsync()
            .AsNoTracking()
            .Where(asset => asset.SiteId == request.SiteId && !asset.IsDeleted)
            .Select(asset => asset.Id)
            .ToListAsync(cancellationToken);

        var activeStatuses = new[] { AlertStatusEnum.Open, AlertStatusEnum.Acknowledged };
        var activeAlertAssetIdSet = siteAssetIds.Count == 0
            ? new HashSet<Guid>()
            : (await _unitOfWork.Alerts
                .GetAllAsync()
                .AsNoTracking()
                .Where(alert =>
                    alert.BatteryAssetId != null &&
                    siteAssetIds.Contains(alert.BatteryAssetId.Value) &&
                    !alert.IsDeleted &&
                    activeStatuses.Contains(alert.Status))
                .Select(alert => alert.BatteryAssetId!.Value)
                .Distinct()
                .ToListAsync(cancellationToken)).ToHashSet();

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
                ActiveAlertCount = activeAlertAssetIdSet.Contains(x.asset.Id) ? 1 : 0,
                CascadeRiskScore = x.asset.CascadeRiskScore,
                CascadeRiskLevel = CascadeRiskDto.ToLevel(x.asset.CascadeRiskScore),
                ElectricalTopology = x.asset.ElectricalTopology,
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
