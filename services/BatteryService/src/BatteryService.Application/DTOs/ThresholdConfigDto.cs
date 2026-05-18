namespace BatteryService.Application.DTOs;

public class ThresholdConfigDto
{
    public string Id { get; set; } = string.Empty;

    public string BatteryTypeId { get; set; } = string.Empty;

    public string BatteryTypeName { get; set; } = string.Empty;

    public decimal VoltageMin { get; set; }

    public decimal VoltageMax { get; set; }

    public decimal TemperatureMax { get; set; }

    public decimal TemperatureMin { get; set; }

    public decimal SocWarningThreshold { get; set; }

    public decimal SocCriticalThreshold { get; set; }

    public decimal? CurrentMaxCharge { get; set; }

    public decimal? CurrentMaxDischarge { get; set; }

    public decimal? SohWarningThreshold { get; set; }

    public decimal? SohCriticalThreshold { get; set; }

    public DateTime EffectiveFromUtc { get; set; }

    public bool IsActive { get; set; }
}
