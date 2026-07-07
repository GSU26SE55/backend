using BatteryService.Application.Common;
using BatteryService.Application.CQRS.Query.Site;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.Site;

public class GetSiteDashboardStatsQueryHandler
    : IRequestHandler<GetSiteDashboardStatsQuery, CommonResponse<SiteDashboardStatsDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public GetSiteDashboardStatsQueryHandler(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<SiteDashboardStatsDto>> Handle(
        GetSiteDashboardStatsQuery request, CancellationToken cancellationToken)
    {
        // ===== Sites =====
        var sites = await _unitOfWork.Sites.GetAllAsync()
            .AsNoTracking()
            .Where(s => !s.IsDeleted)
            .Select(s => new { s.Id, s.Status })
            .ToListAsync(cancellationToken);

        var total = sites.Count;
        var activeCount = sites.Count(s => s.Status == SiteStatusEnum.Active);

        // ===== Battery assets =====
        var assetsQuery = _unitOfWork.BatteryAssets.GetAllAsync()
            .AsNoTracking()
            .Where(a => !a.IsDeleted);

        // Tổng toàn hệ thống (gồm cả asset chưa gán site) — khớp TotalAssets/ActiveAssets của battery/dashboard/stats
        var totalBatteries = await assetsQuery.CountAsync(cancellationToken);
        var activeBatteries = await assetsQuery.CountAsync(a => a.Status == BatteryStatusEnum.Active, cancellationToken);

        // Count per site (chỉ asset đã gán site) — phục vụ tính health từng site
        var assetGroups = await assetsQuery
            .Where(a => a.SiteId != null)
            .GroupBy(a => a.SiteId!.Value)
            .Select(g => new
            {
                SiteId = g.Key,
                Total = g.Count(),
                Active = g.Count(a => a.Status == BatteryStatusEnum.Active)
            })
            .ToListAsync(cancellationToken);
        var assetsBySite = assetGroups.ToDictionary(g => g.SiteId);

        // ===== Asset có alert đang active, distinct theo (site, asset) — cùng định nghĩa với GET /api/sites/{id}/dashboard =====
        var activeAlertStatuses = new[] { AlertStatusEnum.Open, AlertStatusEnum.Acknowledged };
        var alertAssetPairs = await _unitOfWork.Alerts.GetAllAsync()
            .AsNoTracking()
            .Where(alert => !alert.IsDeleted
                && activeAlertStatuses.Contains(alert.Status)
                && alert.BatteryAssetId != null
                && alert.BatteryAsset != null
                && !alert.BatteryAsset.IsDeleted
                && alert.BatteryAsset.SiteId != null)
            .Select(alert => new
            {
                SiteId = alert.BatteryAsset!.SiteId!.Value,
                AssetId = alert.BatteryAssetId!.Value
            })
            .Distinct()
            .ToListAsync(cancellationToken);
        var alertAssetsBySite = alertAssetPairs
            .GroupBy(p => p.SiteId)
            .ToDictionary(g => g.Key, g => g.Count());

        // ===== Health từng site → avg + at-risk =====
        var healthScores = new List<int>(sites.Count);
        var atRiskCount = 0;
        foreach (var site in sites)
        {
            var assets = assetsBySite.GetValueOrDefault(site.Id);
            var health = SiteHealthCalculator.Compute(
                assets?.Total ?? 0,
                assets?.Active ?? 0,
                alertAssetsBySite.GetValueOrDefault(site.Id));
            healthScores.Add(health);
            if (health < SiteHealthCalculator.AtRiskThreshold)
                atRiskCount++;
        }
        var avgHealth = healthScores.Count == 0 ? 0d : Math.Round(healthScores.Average(), 2);

        return new CommonResponse<SiteDashboardStatsDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new SiteDashboardStatsDto
            {
                Total = total,
                ActiveCount = activeCount,
                TotalBatteries = totalBatteries,
                ActiveBatteries = activeBatteries,
                AvgHealth = avgHealth,
                AtRiskCount = atRiskCount
            }
        };
    }
}
