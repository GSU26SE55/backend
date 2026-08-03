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

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = doc.RootElement;

        static decimal Dec(JsonElement e, string p) =>
            e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Number
                ? (decimal)v.GetDouble() : 0m;
        static int Int(JsonElement e, string p) =>
            e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
        static string Str(JsonElement e, string p) =>
            e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : string.Empty;

        // risk.priority (nested) + metadata.model_version (nested)
        var hasRisk = root.TryGetProperty("risk", out var risk);
        var priority = hasRisk ? Str(risk, "priority") : "None";
        var modelVersion = root.TryGetProperty("metadata", out var meta) ? Str(meta, "model_version") : string.Empty;

        // GH-805 — risk.risk_level + risk.action_code + warnings[]: bằng chứng vì sao alert nổ.
        // Response cũ thiếu các field này → null / list rỗng, hành vi lùi về đúng như trước.
        var riskLevel = hasRisk ? Str(risk, "risk_level") : null;
        var actionCode = hasRisk ? Str(risk, "action_code") : null;

        var warnings = new List<AiWarningItem>();
        if (root.TryGetProperty("warnings", out var warningsEl)
            && warningsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var w in warningsEl.EnumerateArray())
            {
                warnings.Add(new AiWarningItem(Str(w, "code"), Str(w, "severity"), Str(w, "message")));
            }
        }
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
            RiskLevel: string.IsNullOrEmpty(riskLevel) ? null : riskLevel,
            ActionCode: string.IsNullOrEmpty(actionCode) ? null : actionCode,
            Warnings: warnings);
    }
}
