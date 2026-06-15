namespace BatteryService.Application.Common.Models;

/// <summary>
/// Sprint 5B #90/#92 — config Open-Meteo sync schedule + endpoint.
/// </summary>
public class WeatherSyncOptions
{
    public const string SectionName = "Weather";

    public string BaseUrl { get; set; } = "https://api.open-meteo.com/";

    public int IntervalMinutes { get; set; } = 15;

    public int DedupWindowMinutes { get; set; } = 10;

    public int HttpTimeoutSeconds { get; set; } = 10;

    public bool Enabled { get; set; } = true;
}
