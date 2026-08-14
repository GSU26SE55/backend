using BatteryService.Application.Common;
using BatteryService.Application.CQRS.Query.Site;
using BatteryService.Application.DTOs;
using BatteryService.Application.Helpers;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.Site;

public class GetSiteDashboardQueryHandler : IRequestHandler<GetSiteDashboardQuery, CommonResponse<SiteDashboardDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IBatteryCurrentUserService _currentUserService;

    public GetSiteDashboardQueryHandler(IBatteryUnitOfWork unitOfWork, IBatteryCurrentUserService currentUserService)
    {
        _unitOfWork = unitOfWork;
        _currentUserService = currentUserService;
    }

    public async Task<CommonResponse<SiteDashboardDto>> Handle(GetSiteDashboardQuery request, CancellationToken cancellationToken)
    {
        // GH-722 — Customer chỉ xem được dashboard của site thuộc mình.
        var scope = BatteryTenantScopeHelper.Resolve(_currentUserService.UserId, _currentUserService.Roles);
        if (scope.IsDenied)
        {
            return new CommonResponse<SiteDashboardDto>
            {
                IsSuccess = false,
                StatusCode = 401,
                Message = "Could not identify the current user."
            };
        }

        var siteQuery = _unitOfWork.Sites
            .GetAllAsync()
            .AsNoTracking()
            .Where(item => item.Id == request.Id && !item.IsDeleted);

        // 404 thay vì 403: không tiết lộ rằng site của tenant khác có tồn tại.
        if (scope.IsCustomerScoped)
        {
            siteQuery = siteQuery.Where(item => item.CustomerId == scope.CustomerId);
        }

        var site = await siteQuery.FirstOrDefaultAsync(cancellationToken);

        if (site is null)
        {
            return new CommonResponse<SiteDashboardDto>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Site not found."
            };
        }

        var assets = await _unitOfWork.BatteryAssets
            .GetAllAsync()
            .AsNoTracking()
            .Where(asset => asset.SiteId == request.Id && !asset.IsDeleted)
            .Select(asset => new { asset.Id, asset.Status })
            .ToListAsync(cancellationToken);

        var assetIds = assets.Select(asset => asset.Id).ToList();
        var activeStatuses = new[] { AlertStatusEnum.Open, AlertStatusEnum.Acknowledged };

        var activeAlertAssetIds = assetIds.Count == 0
            ? new List<Guid>()
            : await _unitOfWork.Alerts
                .GetAllAsync()
                .AsNoTracking()
                .Where(alert =>
                    alert.BatteryAssetId != null &&
                    assetIds.Contains(alert.BatteryAssetId.Value) &&
                    !alert.IsDeleted &&
                    activeStatuses.Contains(alert.Status))
                .Select(alert => alert.BatteryAssetId!.Value)
                .Distinct()
                .ToListAsync(cancellationToken);

        var lastAlertAt = assetIds.Count == 0
            ? null
            : await _unitOfWork.Alerts
                .GetAllAsync()
                .AsNoTracking()
                .Where(alert => alert.BatteryAssetId != null
                                && assetIds.Contains(alert.BatteryAssetId.Value)
                                && !alert.IsDeleted)
                .MaxAsync(alert => (DateTime?)alert.DetectedAt, cancellationToken);

        var totalAssets = assets.Count;
        var activeAssets = assets.Count(asset => asset.Status == BatteryStatusEnum.Active);
        var healthScore = SiteHealthCalculator.Compute(totalAssets, activeAssets, activeAlertAssetIds.Count);

        return new CommonResponse<SiteDashboardDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new SiteDashboardDto
            {
                SiteId = site.Id.ToString(),
                Name = site.Name,
                CustomerId = site.CustomerId.ToString(),
                TotalAssets = totalAssets,
                ActiveAssets = activeAssets,
                AssetsWithActiveAlerts = activeAlertAssetIds.Count,
                LastAlertAt = lastAlertAt,
                HealthScore = healthScore
            }
        };
    }
}
