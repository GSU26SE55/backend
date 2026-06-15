namespace BatteryService.Domain.Enums;

/// <summary>
/// Nguồn của <c>AmbientReading</c> — phân biệt IoT thật vs Weather API (OpenMeteo).
/// Sprint 5B mở rộng — thay cho string field cũ.
/// </summary>
public enum AmbientReadingSourceEnum
{
    /// <summary>Cảm biến IoT thật tại site (vd DHT22, BME280).</summary>
    IotSensor = 1,

    /// <summary>OpenMeteo HTTP API qua WeatherSyncBackgroundService.</summary>
    WeatherApi = 2
}
