using BatteryService.Application.Common.Models;

namespace BatteryService.Application.Interfaces;

/// <summary>Một pin + cửa sổ 30 số đo của nó, dùng cho lượt dự đoán hàng loạt.</summary>
public class AiPredictionBatchItem
{
    public AiPredictionBatchItem(
        string BatteryId, IReadOnlyList<double[]> Readings, AiPackConfig? PackConfig)
    {
        this.BatteryId = BatteryId;
        this.Readings = Readings;
        this.PackConfig = PackConfig;
    }

    public string BatteryId { get; }
    public IReadOnlyList<double[]> Readings { get; }
    public AiPackConfig? PackConfig { get; }
}

/// <summary>Kết quả một lượt dự đoán hàng loạt qua stream.</summary>
public class AiPredictionStreamResult
{
    public AiPredictionStreamResult(
        IReadOnlyList<AiPredictionResult> Predictions, int RequestedCount, string? AbortReason)
    {
        this.Predictions = Predictions;
        this.RequestedCount = RequestedCount;
        this.AbortReason = AbortReason;
    }

    /// <summary>Các prediction NHẬN ĐƯỢC, đúng thứ tự đã gửi. Có thể ngắn hơn số đã yêu cầu.</summary>
    public IReadOnlyList<AiPredictionResult> Predictions { get; }

    public int RequestedCount { get; }

    /// <summary>
    /// Lý do stream dừng sớm; <c>null</c> nếu nhận đủ.
    /// </summary>
    /// <remarks>
    /// ⚠️ Bidi stream KHÔNG có lỗi theo từng message: một cửa sổ sai hình dạng làm ABORT cả
    /// stream sau k−1 response. Nên "thiếu kết quả" ở đây KHÔNG có nghĩa những pin còn lại
    /// bình thường — chúng chỉ đơn giản là chưa được chấm. Caller phải phân biệt hai chuyện đó,
    /// nếu không dashboard sẽ hiển thị pin chưa chấm như pin không có vấn đề.
    /// </remarks>
    public string? AbortReason { get; }

    public bool IsComplete => AbortReason is null && Predictions.Count == RequestedCount;
}

/// <summary>
/// C10 — dự đoán NHIỀU pin trong MỘT kết nối, qua bidi stream <c>PredictStream</c>.
/// </summary>
/// <remarks>
/// Dùng cho màn hình giám sát nhiều pin: N lần gọi unary tốn N lần round-trip, còn stream chỉ
/// tốn một. Với luồng auto-ticket vẫn dùng <c>Prescribe</c> đơn lẻ — ở đó cần cả prescription
/// và mỗi pin đến vào một thời điểm khác nhau, không gom lô được.
/// </remarks>
public interface IAiPredictionStreamClient
{
    Task<AiPredictionStreamResult> PredictManyAsync(
        IReadOnlyList<AiPredictionBatchItem> items,
        CancellationToken cancellationToken = default);
}
