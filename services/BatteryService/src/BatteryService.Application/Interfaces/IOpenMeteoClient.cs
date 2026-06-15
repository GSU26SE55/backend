namespace BatteryService.Application.Interfaces;

/// <summary>
/// Sprint 5B #90 — abstraction trên Open-Meteo HTTP API.
/// Trả về temp + humidity hiện tại theo lat/lon.
/// </summary>
public interface IOpenMeteoClient
{
    /// <summary>
    /// Lấy ambient temperature + humidity hiện tại cho 1 site.
    /// </summary>
    /// <param name="latitude">[-90, 90].</param>
    /// <param name="longitude">[-180, 180].</param>
    Task<AmbientWeatherSample?> GetCurrentAsync(
        decimal latitude,
        decimal longitude,
        CancellationToken cancellationToken = default);
}

public record AmbientWeatherSample(
    decimal TemperatureCelsius,
    decimal Humidity,
    DateTime SampleTimeUtc);
