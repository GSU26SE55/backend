using BatteryService.Domain.Enums;

namespace BatteryService.Application.Common.Models;

/// <summary>
/// BE-AI — pack→cell normalization gửi kèm khi pin là multi-cell pack (GH-65/67).
/// AI chia voltage cho <see cref="NSeries"/> per-cell trước scaler + range guard, nếu không
/// pack 12V/48V bị reject (per-cell range [2.0, 4.5]V). Tính từ BatteryType.NominalVoltage.
/// </summary>
public record AiPackConfig(int NSeries, string? Chemistry, double? CapacityAh);

/// <summary>
/// BE-AI — kết quả /predict (hoặc gRPC Predict) đã map về domain BE, transport-neutral.
/// gRPC client và HTTP client cùng trả type này → job không quan tâm đi đường nào.
///
/// Field map từ AI response (xem docs/overall-ai-be-integration.md §6.1):
///   SohPercent            ← soh_percent
///   Confidence            ← prediction.soh_confidence (MC Dropout, [0,1]) — độ tin cậy SOH
///   Classification        ← classification "Normal"/"Degrading"/"Failed"
///   AnomalyScore          ← anomaly_score (IsolationForest decision_function)
///   AnomalyConfidence     ← anomaly.anomaly_confidence (|IsolationForest score|, [0,1]) — độ tin cậy phân loại
///   RulCyclesEstimate     ← rul_cycles_estimate
///   Priority              ← risk.priority "P1"/"P2"/"P3"/"None" (chỉ Urgency, KHÔNG phải ticket Priority)
///   ModelVersion          ← metadata.model_version ("1.6")
///   LatencyMs             ← inference_ms
///
/// ⚠️ <see cref="Confidence"/> và <see cref="AnomalyConfidence"/> là 2 đại lượng KHÁC NHAU:
/// SohPrediction lưu <see cref="Confidence"/>, AnomalyClassification lưu <see cref="AnomalyConfidence"/>.
/// Field phẳng <c>confidence</c> của AI response = soh_confidence (xem protos/ai_service.proto:161)
/// — KHÔNG dùng nó cho classification.
/// </summary>
public record AiPredictionResult(
    decimal SohPercent,
    decimal Confidence,
    AnomalyClassificationEnum Classification,
    decimal AnomalyScore,
    decimal AnomalyConfidence,
    int RulCyclesEstimate,
    string Priority,
    string ModelVersion,
    int LatencyMs)
{
    /// <summary>Map chuỗi classification của AI → enum BE (Normal=1/Degrading=2/Failed=3).</summary>
    public static AnomalyClassificationEnum ParseClassification(string? raw) => raw?.Trim() switch
    {
        "Normal" => AnomalyClassificationEnum.Normal,
        "Degrading" => AnomalyClassificationEnum.Degrading,
        "Failed" => AnomalyClassificationEnum.Failed,
        // Fallback an toàn: không rõ → Normal (không tự tạo Alert từ giá trị lạ).
        _ => AnomalyClassificationEnum.Normal,
    };
}
