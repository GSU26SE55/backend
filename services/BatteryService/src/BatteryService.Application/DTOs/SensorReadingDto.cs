namespace BatteryService.Application.DTOs;

public class SensorReadingDto
{
    public DateTime Time { get; set; }

    public string BatteryAssetId { get; set; } = string.Empty;

    public decimal Voltage { get; set; }

    public decimal Current { get; set; }

    public decimal Temperature { get; set; }

    public decimal SocPercent { get; set; }

    public int? CycleCount { get; set; }

    public string? SourceDeviceId { get; set; }
}
