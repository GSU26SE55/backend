namespace BatteryService.Application.Services;

/// <summary>
/// Sprint 7 B4 (§31.7) — tính điểm rủi ro lan truyền (cascade risk) rule-based cho 1 asset.
/// </summary>
public interface ICascadeRiskCalculator
{
    /// <summary>Trả điểm 0.0–1.0. Asset không tồn tại → 0.</summary>
    Task<decimal> CalculateAsync(Guid assetId, CancellationToken cancellationToken = default);
}
