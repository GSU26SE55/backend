using System.Globalization;
using System.Text.Json;
using BatteryService.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace BatteryService.Infrastructure.Implements.Services;

/// <summary>
/// Sprint 5B #90 — HTTP impl của <see cref="IOpenMeteoClient"/>.
/// Open-Meteo /v1/forecast endpoint, parse current temp_2m + relative_humidity_2m.
///
/// Polly retry (3 attempts, exponential backoff) + 10s timeout cấu hình
/// ở DI layer qua <c>AddHttpClient&lt;IOpenMeteoClient, OpenMeteoClient&gt;</c>.
/// </summary>
public class OpenMeteoClient : IOpenMeteoClient
{
    private readonly HttpClient _http;
    private readonly ILogger<OpenMeteoClient> _logger;

    public OpenMeteoClient(HttpClient http, ILogger<OpenMeteoClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<AmbientWeatherSample?> GetCurrentAsync(
        decimal latitude,
        decimal longitude,
        CancellationToken cancellationToken = default)
    {
        var lat = latitude.ToString("0.######", CultureInfo.InvariantCulture);
        var lon = longitude.ToString("0.######", CultureInfo.InvariantCulture);

        var url = $"v1/forecast?latitude={lat}&longitude={lon}" +
                  "&current=temperature_2m,relative_humidity_2m" +
                  "&timezone=UTC";

        try
        {
            using var response = await _http.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "OpenMeteo non-success status={Status} for ({Lat},{Lon})",
                    response.StatusCode, lat, lon);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!doc.RootElement.TryGetProperty("current", out var current))
            {
                _logger.LogWarning("OpenMeteo response missing 'current' for ({Lat},{Lon})", lat, lon);
                return null;
            }

            var temp = current.TryGetProperty("temperature_2m", out var t) && t.ValueKind == JsonValueKind.Number
                ? (decimal)t.GetDouble()
                : (decimal?)null;

            var humidity = current.TryGetProperty("relative_humidity_2m", out var h) && h.ValueKind == JsonValueKind.Number
                ? (decimal)h.GetDouble()
                : (decimal?)null;

            var time = current.TryGetProperty("time", out var ts) && ts.ValueKind == JsonValueKind.String
                ? DateTime.SpecifyKind(DateTime.Parse(ts.GetString()!, CultureInfo.InvariantCulture), DateTimeKind.Utc)
                : DateTime.UtcNow;

            if (temp is null || humidity is null)
            {
                _logger.LogWarning("OpenMeteo missing temp/humidity for ({Lat},{Lon})", lat, lon);
                return null;
            }

            return new AmbientWeatherSample(temp.Value, humidity.Value, time);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenMeteo fetch failed for ({Lat},{Lon})", lat, lon);
            return null;
        }
    }
}
