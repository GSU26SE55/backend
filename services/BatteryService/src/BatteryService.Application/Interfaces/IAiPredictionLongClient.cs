namespace BatteryService.Application.Interfaces;

/// <summary>Kết quả SOH từ chuỗi dài (GH-10).</summary>
public class AiLongPredictionResult
{
    public AiLongPredictionResult(
        decimal SohPercent, int SeqLen, string Device, int LatencyMs, string ModelVersion)
    {
        this.SohPercent = SohPercent;
        this.SeqLen = SeqLen;
        this.Device = Device;
        this.LatencyMs = LatencyMs;
        this.ModelVersion = ModelVersion;
    }

    public decimal SohPercent { get; }
    public int SeqLen { get; }

    /// <summary>"cpu" / "cuda" — model long dùng GPU khi có.</summary>
    public string Device { get; }

    public int LatencyMs { get; }

    /// <summary>
    /// Phiên bản model LONG — KHÁC <c>ModelVersion</c> của Predict thường.
    /// </summary>
    /// <remarks>
    /// Hai đường dùng hai bộ trọng số riêng, nên đừng so hai con số này với nhau và đừng
    /// ghi chung một cột: một bản ghi "SOH 41.9 model 2.2" không so sánh được với
    /// "SOH 53.1 model 1.6" dù cùng một viên pin.
    /// </remarks>
    public string ModelVersion { get; }
}

/// <summary>
/// SOH từ chuỗi dài (31..4096 timestep) — dùng cho phân tích lịch sử, KHÔNG phải hot-path.
/// </summary>
/// <remarks>
/// <para>
/// Khác <see cref="IAiPredictionClient"/> ở ba điểm, phải hiểu trước khi dùng:
/// không MC-dropout (⇒ không có confidence/health_stage), không IsolationForest
/// (⇒ không có anomaly/risk/warnings), và dùng bộ trọng số riêng.
/// </para>
/// <para>
/// Bỏ anomaly là CÓ CHỦ Ý phía AI: IsolationForest được fit trên phân bố feature của
/// window=30, chấm một chuỗi 4096 bước bằng nó sẽ ra một con số trông hợp lệ nhưng vô nghĩa.
/// Vì vậy KHÔNG được dùng đường này để quyết định tạo ticket.
/// </para>
/// </remarks>
public interface IAiPredictionLongClient
{
    /// <returns><c>null</c> khi AI không phản hồi hoặc từ chối input.</returns>
    Task<AiLongPredictionResult?> PredictLongAsync(
        string batteryId,
        IReadOnlyList<double[]> readings,
        Common.Models.AiPackConfig? packConfig = null,
        CancellationToken cancellationToken = default);
}
