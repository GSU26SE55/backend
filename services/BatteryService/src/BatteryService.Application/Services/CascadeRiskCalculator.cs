using System.Globalization;
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
        => (await ComputeAsync(assetId, cancellationToken)).Total;

    public async Task<IReadOnlyList<string>> ExplainAsync(Guid assetId, CancellationToken cancellationToken = default)
        => (await ComputeAsync(assetId, cancellationToken)).Reasons;

    /// <summary>
    /// Nguồn logic duy nhất cho cả điểm số (CalculateAsync, dùng để threshold-crossing) và lý do
    /// hiển thị (ExplainAsync, dùng cho tooltip UI) — tránh 2 implementation lệch nhau theo thời
    /// gian nếu tách riêng.
    /// </summary>
    private async Task<(decimal Total, List<string> Reasons)> ComputeAsync(
        Guid assetId, CancellationToken cancellationToken)
    {
        var asset = await _unitOfWork.BatteryAssets.GetByIdAsync(assetId);
        if (asset is null || asset.IsDeleted)
        {
            return (0m, new List<string>());
        }

        decimal score = 0m;
        var reasons = new List<string>();

        // Rule 1: Topology factor — rủi ro LAN TRUYỀN thermal runaway, không phải rủi ro mất
        // chức năng vận hành (2 khái niệm khác nhau, dễ nhầm — xem non-obvious-decisions.md).
        // Nghiên cứu thực nghiệm nhất quán cho thấy PARALLEL mới là kiểu nguy hiểm nhất khi lan
        // truyền: pin lành trong cùng nhánh song song đổ dòng điện (năng lượng tích trữ) thẳng
        // vào pin đang runaway qua đường điện trở thấp, cộng thêm nhiệt lên trên phản ứng toả
        // nhiệt tự nhiên → lan nhanh và dữ dội hơn. Series không chia sẻ đường dòng điện đó, lan
        // truyền chỉ qua dẫn nhiệt vật lý (chậm hơn nhiều, có nghiên cứu ghi nhận GẦN NHƯ KHÔNG
        // lan truyền ở cấu hình series thuần). Đỉnh nhiệt độ đo được: Parallel 720°C > Series-
        // Parallel 683°C > Series 669°C.
        // Nguồn: Sun et al., "Experimental study on thermal runaway propagation of lithium-ion
        // battery modules with different parallel-series hybrid connections", J. Cleaner
        // Production (2020), https://doi.org/10.1016/j.jclepro.2020.124188; Feng et al.,
        // "An experimental and analytical study of thermal runaway propagation... NCM
        // pouch-cells in parallel", Int. J. Heat Mass Transfer (2019),
        // https://doi.org/10.1016/j.ijheatmasstransfer.2018.11.077.
        var topologyScore = asset.ElectricalTopology switch
        {
            ElectricalTopologyEnum.Independent => 0.0m,
            ElectricalTopologyEnum.SeriesString => 0.2m,
            ElectricalTopologyEnum.SeriesParallel => 0.4m,
            ElectricalTopologyEnum.ParallelBank => 0.6m,
            _ => 0.0m
        };
        score += topologyScore;
        if (topologyScore > 0m)
        {
            // InvariantCulture bắt buộc — culture hệ thống có thể dùng ',' làm decimal separator,
            // reason string này là dữ liệu hiển thị/parse ổn định cho FE, không phải localized text.
            reasons.Add($"{asset.ElectricalTopology} wiring adds +{topologyScore.ToString("0.00", CultureInfo.InvariantCulture)}");
        }

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
            {
                score += 0.2m;
                reasons.Add($"{siblingAnomalies} neighbouring battery(ies) in the same site have an open alert adds +0.20");
            }
            if (siblingAnomalies >= 3)
            {
                score += 0.2m;  // cumulative
                reasons.Add("3+ neighbouring batteries have open alerts adds +0.20");
            }
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
        {
            score += 0.3m;
            reasons.Add("Critical overheat alert is open on this battery adds +0.30");
        }

        return (Math.Min(1.0m, score), reasons);  // clamp total only — reasons show raw contribution
    }
}
