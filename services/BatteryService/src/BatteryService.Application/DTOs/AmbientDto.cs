using BatteryService.Domain.Enums;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.DTOs;

public class AmbientReadingDto
{
    public DateTime Time { get; set; }
    public string SiteId { get; set; } = string.Empty;
    public decimal AmbientTemperature { get; set; }
    public decimal? Humidity { get; set; }
    public decimal? SolarIrradiance { get; set; }
    public AmbientReadingSourceEnum Source { get; set; } = AmbientReadingSourceEnum.WeatherApi;
    public string? SourceDeviceId { get; set; }
}

public class AmbientThresholdConfigDto
{
    public string Id { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public decimal? HighAmbientTempWarning { get; set; }
    public decimal? HighAmbientTempCritical { get; set; }
    public decimal? HighHumidityWarning { get; set; }
    public decimal? HighHumidityCritical { get; set; }
    public decimal? ComboTempThreshold { get; set; }
    public decimal? ComboHumidityThreshold { get; set; }
    public bool Enabled { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AmbientReadingListResponse : CommonResponse<PaginationResponse<AmbientReadingDto>> { }
public class AmbientReadingLatestResponse : CommonResponse<AmbientReadingDto> { }
public class AmbientThresholdConfigResponse : CommonResponse<AmbientThresholdConfigDto> { }
public class AmbientThresholdConfigListResponse : CommonResponse<PaginationResponse<AmbientThresholdConfigDto>> { }
