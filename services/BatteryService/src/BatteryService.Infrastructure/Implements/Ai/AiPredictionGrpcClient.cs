using AiModule.V1;
using BatteryService.Application.Common.Models;
using Grpc.Core;

namespace BatteryService.Infrastructure.Implements.Ai;

/// <summary>
/// BE-AI — gRPC impl của Predict (PRIMARY transport). Gọi AiService.Predict trên :50051.
///
/// KHÔNG tự fallback — ném <see cref="RpcException"/> lên cho <c>FallbackAiPredictionClient</c>
/// quyết định: lỗi transport (Unavailable/DeadlineExceeded/Internal) → thử HTTP;
/// InvalidArgument (input sai) → KHÔNG fallback (HTTP dùng chung schema, reject y hệt).
/// </summary>
public class AiPredictionGrpcClient
{
    private readonly AiService.AiServiceClient _client;

    public AiPredictionGrpcClient(AiService.AiServiceClient client) => _client = client;

    // virtual: cho phép Moq override trong unit test FallbackAiPredictionClient
    // (gRPC AsyncUnaryCall khó stub trực tiếp).
    public virtual async Task<AiPredictionResult> PredictAsync(
        string batteryId,
        IReadOnlyList<double[]> readings,
        AiPackConfig? packConfig,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var request = new PredictRequest { BatteryId = batteryId };
        foreach (var row in readings)
        {
            var reading = new Reading();
            // GH-777 — chuyển tiếp NGUYÊN số cột của hàng: 4 cột
            // [voltage, current, temperature, time] hoặc 6 cột khi có thêm
            // [cycle_count, soc_percent]. Proto khai `repeated double` nên không cần sửa gì.
            reading.Values.AddRange(row);
            request.Readings.Add(reading);
        }
        if (packConfig is not null)
        {
            request.PackConfig = new PackConfig { NSeries = packConfig.NSeries };
            if (packConfig.Chemistry is not null)
                request.PackConfig.Chemistry = packConfig.Chemistry;
            if (packConfig.CapacityAh is not null)
                request.PackConfig.CapacityAh = packConfig.CapacityAh.Value;
        }

        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        var resp = await _client.PredictAsync(
            request, deadline: deadline, cancellationToken: cancellationToken);

        // Flat backward-compat fields — cùng payload REST (parity test đảm bảo).
        return new AiPredictionResult(
            SohPercent: (decimal)resp.SohPercent,
            Confidence: (decimal)resp.Confidence,
            Classification: AiPredictionResult.ParseClassification(resp.Classification),
            AnomalyScore: (decimal)resp.AnomalyScore,
            // anomaly.anomaly_confidence — chỉ có ở block nested (không có field phẳng).
            AnomalyConfidence: (decimal)(resp.Anomaly?.AnomalyConfidence ?? 0d),
            RulCyclesEstimate: resp.RulCyclesEstimate,
            Priority: resp.Risk?.Priority ?? "None",
            ModelVersion: resp.Metadata?.ModelVersion ?? string.Empty,
            LatencyMs: (int)Math.Round(resp.InferenceMs),
            // Nguyên văn response → soh_predictions.raw_response. Đường gRPC không có sẵn
            // JSON như HTTP nên phải format lại. JsonFormatter dùng đúng tên field khai
            // trong proto, nên hai transport ghi ra cùng một hình dạng và truy vấn jsonb
            // sau này không phải phân biệt request đã đi đường nào.
            RawResponse: Google.Protobuf.JsonFormatter.Default.Format(resp),
            // Khối nested — KHÔNG có field phẳng tương ứng, nên trước đây chúng biến mất
            // hoàn toàn ở ranh giới bridge dù AI vẫn trả đủ.
            HealthStage: resp.Prediction?.HealthStage,
            StageConfidence: resp.Prediction is null ? null : (decimal)resp.Prediction.StageConfidence,
            IsBorderline: resp.Prediction?.IsBorderline ?? false,
            SohStd: resp.Prediction is null ? null : (decimal)resp.Prediction.SohStd,
            RiskLevel: resp.Risk?.RiskLevel,
            ActionCode: resp.Risk?.ActionCode,
            SohTrend: resp.SohTrend,
            DegradationRatePerCycle: (decimal)resp.DegradationRatePerCycle,
            CyclesToMaintenance: resp.CyclesToMaintenance,
            IsTemperatureOod: resp.Metadata?.IsTemperatureOod ?? false);
    }
}
