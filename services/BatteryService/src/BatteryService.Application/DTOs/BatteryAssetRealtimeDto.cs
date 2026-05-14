using BatteryService.Domain.Enums;

namespace BatteryService.Application.DTOs;

public class BatteryAssetRealtimeDto
{
    public Guid AssetId { get; set; }

    public string SerialNumber { get; set; } = string.Empty;

    public BatteryStatusEnum Status { get; set; }

    public DateTime? Time { get; set; }

    public decimal? Voltage { get; set; }

    public decimal? Current { get; set; }

    public decimal? Temperature { get; set; }

    public decimal? SocPercent { get; set; }

    public int? CycleCount { get; set; }

    public int ActiveAlerts { get; set; }
}
