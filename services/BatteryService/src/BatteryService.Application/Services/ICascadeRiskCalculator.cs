namespace BatteryService.Application.Services;

/// <summary>
/// Sprint 7 B4 (§31.7) — tính điểm rủi ro lan truyền (cascade risk) rule-based cho 1 asset.
/// </summary>
public interface ICascadeRiskCalculator
{
    /// <summary>Trả điểm 0.0–1.0. Asset không tồn tại → 0.</summary>
    Task<decimal> CalculateAsync(Guid assetId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Giải thích điểm hiện tại theo từng rule đã cộng điểm — chỉ dùng để hiển thị (tooltip trên
    /// UI), KHÔNG dùng cho threshold-crossing (vẫn là <see cref="CalculateAsync"/> qua
    /// CascadeRiskService). Chỉ liệt kê rule có đóng góp &gt; 0. Asset không tồn tại → rỗng.
    /// </summary>
    Task<IReadOnlyList<string>> ExplainAsync(Guid assetId, CancellationToken cancellationToken = default);
}
