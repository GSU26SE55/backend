using BatteryService.Domain.Enums;

namespace BatteryService.Application.DTOs;

public class BatteryAssetRealtimeDto
{
    /// <summary>ID BatteryAsset.</summary>
    public string AssetId { get; set; } = string.Empty;

    /// <summary>Serial number của asset (unique).</summary>
    public string SerialNumber { get; set; } = string.Empty;

    /// <summary>Filter theo status enum.</summary>
    public BatteryStatusEnum Status { get; set; }

    /// <summary>Timestamp của reading (UTC).</summary>
    public DateTime? Time { get; set; }

    /// <summary>Điện áp (V).</summary>
    public decimal? Voltage { get; set; }

    /// <summary>Cường độ dòng (A). Âm = xả, dương = sạc.</summary>
    public decimal? Current { get; set; }

    /// <summary>Nhiệt độ (°C).</summary>
    public decimal? Temperature { get; set; }

    /// <summary>State of Charge — % pin còn (0..100).</summary>
    public decimal? SocPercent { get; set; }

    /// <summary>Số chu kỳ sạc/xả của pin.</summary>
    public int? CycleCount { get; set; }

    /// <summary>State of Health — % sức khoẻ pin (0..100).</summary>
    public decimal? SohPercent { get; set; }

    /// <summary>Trạng thái sạc (Idle / Charging / Discharging / Full).</summary>
    public ChargingStateEnum? ChargingState { get; set; }

    /// <summary>Field ActiveAlerts.</summary>
    public int ActiveAlerts { get; set; }
}
