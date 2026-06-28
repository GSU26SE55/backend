using BatteryService.Application.Interfaces;
using BatteryService.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace BatteryService.Application.Services;

/// <summary>
/// Sprint 7 B4 (§31.7) — rule-based cascade risk.
///
/// Lưu ý adaptation: spec gốc dùng <c>BatteryGroupId</c> cho proximity, nhưng project đã bỏ
/// BatteryGroup (migration <c>RemoveBatteryGroup</c>). Proximity ở đây nhóm theo <c>SiteId</c>
/// — khớp với endpoint <c>/sites/{id}/cascade-risk-summary</c>.
/// </summary>
public class CascadeRiskCalculator : ICascadeRiskCalculator
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public CascadeRiskCalculator(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<decimal> CalculateAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        var asset = await _unitOfWork.BatteryAssets.GetByIdAsync(assetId);
        if (asset is null || asset.IsDeleted)
        {
            return 0m;
        }

        decimal score = 0m;

        // Rule 1: Topology factor — series = mất 1 pin có thể ngắt cả string.
        score += asset.ElectricalTopology switch
        {
            ElectricalTopologyEnum.Independent => 0.0m,
            ElectricalTopologyEnum.ParallelBank => 0.2m,
            ElectricalTopologyEnum.SeriesParallel => 0.4m,
            ElectricalTopologyEnum.SeriesString => 0.6m,
            _ => 0.0m
        };

        // Rule 2: Proximity — đếm asset cùng Site có Open alert trong 1h gần đây.
        if (asset.SiteId.HasValue)
        {
            var since = DateTime.UtcNow.AddHours(-1);

            var siblingAssetIds = await _unitOfWork.BatteryAssets.GetAllAsync()
                .Where(b => !b.IsDeleted && b.SiteId == asset.SiteId && b.Id != assetId)
                .Select(b => b.Id)
                .ToListAsync(cancellationToken);

            var siblingAnomalies = siblingAssetIds.Count == 0
                ? 0
                : await _unitOfWork.Alerts.GetAllAsync()
                    .Where(a => !a.IsDeleted
                        && a.Status == AlertStatusEnum.Open
                        && a.DetectedAt >= since
                        && a.BatteryAssetId != null
                        && siblingAssetIds.Contains(a.BatteryAssetId.Value))
                    .Select(a => a.BatteryAssetId)
                    .Distinct()
                    .CountAsync(cancellationToken);

            if (siblingAnomalies >= 1)
                score += 0.2m;
            if (siblingAnomalies >= 3)
                score += 0.2m;  // cumulative
        }

        // Rule 3: Thermal runaway — overheat Critical Open lây lan nhiệt.
        var hasThermalRunaway = await _unitOfWork.Alerts.GetAllAsync()
            .Where(a => !a.IsDeleted
                && a.BatteryAssetId == assetId
                && a.AnomalyType == AnomalyTypeEnum.Overheat
                && a.Severity == AlertSeverityEnum.Critical
                && a.Status == AlertStatusEnum.Open)
            .AnyAsync(cancellationToken);
        if (hasThermalRunaway)
            score += 0.3m;

        return Math.Min(1.0m, score);  // clamp
    }
}
