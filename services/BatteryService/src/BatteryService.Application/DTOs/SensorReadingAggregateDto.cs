namespace BatteryService.Application.DTOs;

public class SensorReadingAggregateDto
{
    /// <summary>Timestamp của reading (UTC).</summary>
    public DateTime Time { get; set; }

    /// <summary>Điện áp trung bình (V) trong bucket.</summary>
    public decimal AvgVoltage { get; set; }

    /// <summary>Dòng trung bình (A) trong bucket.</summary>
    public decimal AvgCurrent { get; set; }

    /// <summary>Nhiệt độ trung bình (°C) trong bucket.</summary>
    public decimal AvgTemperature { get; set; }

    /// <summary>SOC trung bình (%) trong bucket.</summary>
    public decimal AvgSocPercent { get; set; }

    /// <summary>SOH trung bình (%) trong bucket (null nếu không có data).</summary>
    public decimal? AvgSohPercent { get; set; }
}
