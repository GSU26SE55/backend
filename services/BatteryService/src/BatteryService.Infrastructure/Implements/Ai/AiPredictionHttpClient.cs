using System.Net.Http.Json;
using System.Text.Json;
using BatteryService.Application.Common.Models;
using Microsoft.Extensions.Logging;

namespace BatteryService.Infrastructure.Implements.Ai;

/// <summary>
/// BE-AI — HTTP/FastAPI impl của Predict (FALLBACK transport). POST /predict/ trên :8000.
/// Cùng payload/JSON như REST — parse flat fields (soh_percent, classification, ...).
/// Clone pattern OpenMeteoClient: HttpClient + timeout/Polly cấu hình ở DI layer.
/// </summary>
public class AiPredictionHttpClient
{
    private readonly HttpClient _http;
    private readonly ILogger<AiPredictionHttpClient> _logger;

    public AiPredictionHttpClient(HttpClient http, ILogger<AiPredictionHttpClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public virtual async Task<AiPredictionResult?> PredictAsync(
        string batteryId,
        IReadOnlyList<double[]> readings,
        AiPackConfig? packConfig,
        CancellationToken cancellationToken)
    {
        object payload = packConfig is null
            ? new { battery_id = batteryId, readings }
            : new
            {
                battery_id = batteryId,
                readings,
                pack_config = new { n_series = packConfig.NSeries, chemistry = packConfig.Chemistry, capacity_ah = packConfig.CapacityAh },
            };

        using var response = await _http.PostAsJsonAsync("/predict/", payload, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "AI /predict HTTP non-success {Status} for {BatteryId}: {Body}",
                response.StatusCode, batteryId, body);
            return null;
        }

        // Đọc thành CHUỖI thay vì stream: cần giữ nguyên văn để ghi vào
        // soh_predictions.raw_response. AI trả ~35 field mà AiPredictionResult chỉ mang 9;
        // phần còn lại chỉ tồn tại ở đây, parse xong stream là mất hẳn.
        var rawBody = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(rawBody);
        var root = doc.RootElement;

        static decimal Dec(JsonElement e, string p) =>
            e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Number
                ? (decimal)v.GetDouble() : 0m;
        static int Int(JsonElement e, string p) =>
            e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
        static string Str(JsonElement e, string p) =>
            e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : string.Empty;
        static bool Bool(JsonElement e, string p) =>
            e.TryGetProperty(p, out var v)
            && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False)
            && v.GetBoolean();

        // risk.priority (nested) + metadata.model_version (nested)
        var hasRisk = root.TryGetProperty("risk", out var risk);
        var priority = hasRisk ? Str(risk, "priority") : "None";
        var hasMeta = root.TryGetProperty("metadata", out var meta);
        var modelVersion = hasMeta ? Str(meta, "model_version") : string.Empty;
        // GH-86 — khối prediction lồng nhau chứa toàn bộ thông tin bất định. Không có field
        // phẳng nào tương ứng, nên bỏ khối này là mất hẳn.
        var hasPrediction = root.TryGetProperty("prediction", out var prediction);
        // anomaly.anomaly_confidence (nested) — KHÔNG có field phẳng tương ứng; field phẳng
        // "confidence" là soh_confidence, dùng cho SohPrediction chứ không phải classification.
        var anomalyConfidence = root.TryGetProperty("anomaly", out var anomaly)
            ? Dec(anomaly, "anomaly_confidence") : 0m;

        return new AiPredictionResult(
            SohPercent: Dec(root, "soh_percent"),
            Confidence: Dec(root, "confidence"),
            Classification: AiPredictionResult.ParseClassification(Str(root, "classification")),
            AnomalyScore: Dec(root, "anomaly_score"),
            AnomalyConfidence: anomalyConfidence,
            RulCyclesEstimate: Int(root, "rul_cycles_estimate"),
            Priority: string.IsNullOrEmpty(priority) ? "None" : priority,
            ModelVersion: modelVersion,
            LatencyMs: (int)Math.Round((double)Dec(root, "inference_ms")),
            RawResponse: rawBody,
            // Khối nested — không có field phẳng tương ứng. Phải parity với gRPC, nếu không
            // cùng một pin sẽ lưu khác nhau tuỳ transport nào đang sống.
            HealthStage: hasPrediction ? Str(prediction, "health_stage") : null,
            StageConfidence: hasPrediction ? Dec(prediction, "stage_confidence") : null,
            IsBorderline: hasPrediction && Bool(prediction, "is_borderline"),
            SohStd: hasPrediction ? Dec(prediction, "soh_std") : null,
            RiskLevel: hasRisk ? Str(risk, "risk_level") : null,
            ActionCode: hasRisk ? Str(risk, "action_code") : null,
            SohTrend: Str(root, "soh_trend"),
            DegradationRatePerCycle: Dec(root, "degradation_rate_per_cycle"),
            CyclesToMaintenance: Int(root, "cycles_to_maintenance"),
            IsTemperatureOod: hasMeta && Bool(meta, "is_temperature_ood"));
    }
}
