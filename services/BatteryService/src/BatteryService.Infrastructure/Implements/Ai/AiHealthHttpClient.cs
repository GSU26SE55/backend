using System.Text.Json;
using BatteryService.Application.Common.Models;

namespace BatteryService.Infrastructure.Implements.Ai;

/// <summary>
/// BE-AI — HTTP/FastAPI impl của Health (FALLBACK). <c>GET /health</c> trên :8000.
/// </summary>
public class AiHealthHttpClient
{
    private readonly HttpClient _http;

    public AiHealthHttpClient(HttpClient http) => _http = http;

    public virtual async Task<AiHealthResult> GetHealthAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync("/health", cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = doc.RootElement;

        static string Str(JsonElement e, string p) =>
            e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()! : string.Empty;
        static bool Bool(JsonElement e, string p) =>
            e.TryGetProperty(p, out var v)
            && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False)
            && v.GetBoolean();

        return new AiHealthResult(
            Status: Str(root, "status"),
            ModelVersion: Str(root, "model_version"),
            ScalerLoaded: Bool(root, "scaler_loaded"),
            MambaLoaded: Bool(root, "mamba_loaded"),
            IsolationForestLoaded: Bool(root, "isolation_forest_loaded"),
            LfpLoaded: Bool(root, "lfp_loaded"),
            LfpModelVersion: Str(root, "lfp_model_version"),
            SocMode: Str(root, "soc_mode"),
            LfpSocMode: Str(root, "lfp_soc_mode"),
            LongLoaded: Bool(root, "long_loaded"),
            LongModelVersion: Str(root, "long_model_version"));
    }
}
