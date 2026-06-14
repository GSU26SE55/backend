namespace BatteryService.Application.DTOs;

public class ThresholdConfigDto
{
    /// <summary>Định danh resource.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>ID BatteryType (Guid).</summary>
    public string BatteryTypeId { get; set; } = string.Empty;

    /// <summary>Tên của batterytype.</summary>
    public string BatteryTypeName { get; set; } = string.Empty;

    /// <summary>Điện áp tối thiểu cho phép (V).</summary>
    public decimal VoltageMin { get; set; }

    /// <summary>Điện áp tối đa cho phép (V).</summary>
    public decimal VoltageMax { get; set; }

    /// <summary>Nhiệt độ tối đa (°C).</summary>
    public decimal TemperatureMax { get; set; }

    /// <summary>Nhiệt độ tối thiểu (°C).</summary>
    public decimal TemperatureMin { get; set; }

    /// <summary>SOC threshold Warning (vd 20%).</summary>
    public decimal SocWarningThreshold { get; set; }

    /// <summary>SOC threshold Critical (vd 10%).</summary>
    public decimal SocCriticalThreshold { get; set; }

    /// <summary>Dòng sạc tối đa (A).</summary>
    public decimal? CurrentMaxCharge { get; set; }

    /// <summary>Dòng xả tối đa (A).</summary>
    public decimal? CurrentMaxDischarge { get; set; }

    /// <summary>SOH threshold Warning (vd 85%).</summary>
    public decimal? SohWarningThreshold { get; set; }

    /// <summary>SOH threshold Critical (vd 75%).</summary>
    public decimal? SohCriticalThreshold { get; set; }

    /// <summary>Field EffectiveFromUtc.</summary>
    public DateTime EffectiveFromUtc { get; set; }

    /// <summary>Active flag.</summary>
    public bool IsActive { get; set; }
}
