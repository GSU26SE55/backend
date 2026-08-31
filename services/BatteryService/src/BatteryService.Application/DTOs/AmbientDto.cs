using BatteryService.Domain.Enums;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.DTOs;

public class AmbientReadingDto
{
    /// <summary>Timestamp của reading (UTC).</summary>
    public DateTime Time { get; set; }
    /// <summary>ID Site (Guid).</summary>
    public string SiteId { get; set; } = string.Empty;
    /// <summary>Nhiệt độ môi trường (°C). Nullable — báo cáo chỉ-có-gas không có giá trị này.</summary>
    public decimal? AmbientTemperature { get; set; }
    /// <summary>Độ ẩm tương đối (%).</summary>
    public decimal? Humidity { get; set; }
    /// <summary>Bức xạ mặt trời (W/m²).</summary>
    public decimal? SolarIrradiance { get; set; }
    /// <summary>Nồng độ khí gas quy đổi (%).</summary>
    public decimal? GasConcentration { get; set; }
    /// <summary>true = ướt, false = khô. Nullable — chỉ cảm biến nước mới gửi.</summary>
    public bool? WaterLeakDetected { get; set; }
    /// <summary>Nguồn dữ liệu (IotSensor | Manual | External).</summary>
    public AmbientReadingSourceEnum Source { get; set; } = AmbientReadingSourceEnum.WeatherApi;
    /// <summary>ID thiết bị nguồn (≤ 64 ký tự).</summary>
    public string? SourceDeviceId { get; set; }
}

public class AmbientThresholdConfigDto
{
    /// <summary>Định danh resource.</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>ID Site (Guid).</summary>
    public string SiteId { get; set; } = string.Empty;
    /// <summary>Ambient temp Warning threshold (°C).</summary>
    public decimal? HighAmbientTempWarning { get; set; }
    /// <summary>Ambient temp Critical threshold (°C).</summary>
    public decimal? HighAmbientTempCritical { get; set; }
    /// <summary>Humidity Warning threshold (%).</summary>
    public decimal? HighHumidityWarning { get; set; }
    /// <summary>Humidity Critical threshold (%).</summary>
    public decimal? HighHumidityCritical { get; set; }
    /// <summary>Gas Concentration Warning threshold (%).</summary>
    public decimal? HighGasWarning { get; set; }
    /// <summary>Gas Concentration Critical threshold (%).</summary>
    public decimal? HighGasCritical { get; set; }
    /// <summary>Combo temp threshold (cùng với humidity).</summary>
    public decimal? ComboTempThreshold { get; set; }
    /// <summary>Combo humidity threshold.</summary>
    public decimal? ComboHumidityThreshold { get; set; }
    /// <summary>Tính năng bật/tắt.</summary>
    public bool Enabled { get; set; }
    /// <summary>Timestamp tạo (UTC).</summary>
    public DateTime CreatedAt { get; set; }
}

public class AmbientReadingListResponse : CommonResponse<PaginationResponse<AmbientReadingDto>> { }
public class AmbientReadingLatestResponse : CommonResponse<AmbientReadingDto> { }
public class AmbientThresholdConfigResponse : CommonResponse<AmbientThresholdConfigDto> { }
public class AmbientThresholdConfigListResponse : CommonResponse<PaginationResponse<AmbientThresholdConfigDto>> { }
