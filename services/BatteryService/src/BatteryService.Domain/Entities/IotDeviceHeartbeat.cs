namespace BatteryService.Domain.Entities;

/// <summary>
/// Sprint IoT-1 (#242) — heartbeat sample từ <see cref="IotDevice"/>.
/// Hypertable TimescaleDB partition theo <c>Time</c>. Append-only — không AuditableEntity.
/// </summary>
public class IotDeviceHeartbeat
{
    public DateTime Time { get; set; }

    public Guid IotDeviceId { get; set; }

    /// <summary>Firmware version device báo trong heartbeat (vd "1.2.3").</summary>
    public string? FirmwareVersion { get; set; }

    /// <summary>Tín hiệu WiFi RSSI (dBm). Thường âm: -50 mạnh, -90 yếu.</summary>
    public int? RssiDbm { get; set; }

    /// <summary>% RAM free (0-100). Null nếu device không gửi.</summary>
    public decimal? FreeMemoryPercent { get; set; }

    /// <summary>Uptime device (giây).</summary>
    public long? UptimeSeconds { get; set; }

    /// <summary>Số sensor reading đã queue local mà chưa flush được do mất mạng.</summary>
    public int? QueuedReadingCount { get; set; }

    /// <summary>Đồng hồ device gửi lên — backend tính skew vs server clock.</summary>
    public DateTime? DeviceTimestamp { get; set; }

    /// <summary>Skew device-vs-server (giây), tính khi nhận heartbeat.</summary>
    public double? ClockSkewSeconds { get; set; }

    public IotDevice IotDevice { get; set; } = null!;
}
