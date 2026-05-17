namespace BatteryService.Application.DTOs;

public class SensorReadingAggregateDto
{
    public DateTime Time { get; set; }

    public decimal AvgVoltage { get; set; }

    public decimal AvgCurrent { get; set; }

    public decimal AvgTemperature { get; set; }

    public decimal AvgSocPercent { get; set; }

    public decimal? AvgSohPercent { get; set; }
}
