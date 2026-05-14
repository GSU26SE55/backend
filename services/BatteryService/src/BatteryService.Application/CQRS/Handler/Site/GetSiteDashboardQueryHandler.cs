using BatteryService.Application.CQRS.Query.Site;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.Site;

public class GetSiteDashboardQueryHandler : IRequestHandler<GetSiteDashboardQuery, CommonResponse<SiteDashboardDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public GetSiteDashboardQueryHandler(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<SiteDashboardDto>> Handle(GetSiteDashboardQuery request, CancellationToken cancellationToken)
    {
        var site = await _unitOfWork.Sites
            .GetAllAsync()
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == request.Id && !item.IsDeleted, cancellationToken);

        if (site is null)
        {
            return new CommonResponse<SiteDashboardDto>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy site."
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
                    assetIds.Contains(alert.BatteryAssetId) &&
                    !alert.IsDeleted &&
                    activeStatuses.Contains(alert.Status))
                .Select(alert => alert.BatteryAssetId)
                .Distinct()
                .ToListAsync(cancellationToken);

        var lastAlertAt = assetIds.Count == 0
            ? null
            : await _unitOfWork.Alerts
                .GetAllAsync()
                .AsNoTracking()
                .Where(alert => assetIds.Contains(alert.BatteryAssetId) && !alert.IsDeleted)
                .MaxAsync(alert => (DateTime?)alert.DetectedAt, cancellationToken);

        var totalAssets = assets.Count;
        var activeAssets = assets.Count(asset => asset.Status == BatteryStatusEnum.Active);
        var inactivePenalty = totalAssets == 0 ? 0 : (totalAssets - activeAssets) * 5;
        var alertPenalty = activeAlertAssetIds.Count * 10;
        var healthScore = Math.Clamp(100 - inactivePenalty - alertPenalty, 0, 100);

        return new CommonResponse<SiteDashboardDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new SiteDashboardDto
            {
                SiteId = site.Id,
                Name = site.Name,
                CustomerId = site.CustomerId,
                TotalAssets = totalAssets,
                ActiveAssets = activeAssets,
                AssetsWithActiveAlerts = activeAlertAssetIds.Count,
                TotalCapacityKw = site.CapacityKw,
                LastAlertAt = lastAlertAt,
                HealthScore = healthScore
            }
        };
    }
}
