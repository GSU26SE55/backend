using BatteryService.Domain.Enums;

namespace BatteryService.Domain.Entities;

/// <summary>
/// Sprint 5B #89 — time-series ambient (temperature + humidity + irradiance) per site.
/// Hypertable (TimescaleDB) — không inherit AuditableEntity.
/// Source: OpenMeteo sync hoặc IoT ingest (xem #91).
/// </summary>
public class AmbientReading
{
    public DateTime Time { get; set; }
    public Guid SiteId { get; set; }

    public decimal AmbientTemperature { get; set; }

    /// <summary>%RH 0–100. Nullable — OpenMeteo có thể bỏ qua.</summary>
    public decimal? Humidity { get; set; }

    /// <summary>W/m² — `shortwave_radiation` từ OpenMeteo hoặc pyranometer. Nullable.</summary>
    public decimal? SolarIrradiance { get; set; }

    /// <summary>Sprint 5B — đổi từ string sang enum (IotSensor / WeatherApi).</summary>
    public AmbientReadingSourceEnum Source { get; set; } = AmbientReadingSourceEnum.WeatherApi;

    /// <summary>DeviceId của IoT sensor; "openmeteo" cho WeatherApi.</summary>
    public string? SourceDeviceId { get; set; }

    public Site Site { get; set; } = null!;
}
