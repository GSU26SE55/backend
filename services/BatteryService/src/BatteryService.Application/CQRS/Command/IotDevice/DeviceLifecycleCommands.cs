using System.Text.Json.Serialization;
using BatteryService.Application.DTOs;
using BatteryService.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Command.IotDevice;

/// <summary>Device gọi sau khi flash + boot — báo cáo firmware version + hardware revision lần đầu.</summary>
public class ProvisionIotDeviceCommand : IRequest<CommonResponse<IotDeviceProvisionResultDto>>
{
    /// <summary>Lấy từ claim "iot:device_id" (API key). Body không bind.</summary>
    [JsonIgnore]
    [BindNever]
    public Guid DeviceId { get; set; }

    /// <summary>Lấy từ claim "iot:device_code" (API key). Body không bind.</summary>
    [JsonIgnore]
    [BindNever]
    public string DeviceCode { get; set; } = string.Empty;

    /// <summary>Phiên bản firmware (vd "1.2.3").</summary>
    public string FirmwareVersion { get; set; } = string.Empty;
    /// <summary>Hardware revision (vd "v1.0-S3-MAX485").</summary>
    public string? HardwareRevision { get; set; }
    /// <summary>Timestamp ghi nhận tại device (UTC) — backend check clock skew.</summary>
    public DateTime DeviceTimestamp { get; set; }
}

/// <summary>Heartbeat — telemetry health + tiến độ queue + clock skew.</summary>
public class IotDeviceHeartbeatCommand : IRequest<CommonResponse<IotHeartbeatAckDto>>
{
    /// <summary>ID IoT device (Guid).</summary>
    [JsonIgnore]
    [BindNever]
    public Guid DeviceId { get; set; }

    /// <summary>Mã device duy nhất (vd ESP32-001).</summary>
    [JsonIgnore]
    [BindNever]
    public string DeviceCode { get; set; } = string.Empty;

    /// <summary>Phiên bản firmware (vd "1.2.3").</summary>
    public string? FirmwareVersion { get; set; }
    /// <summary>WiFi RSSI (dBm). Thường âm: -50 mạnh, -90 yếu.</summary>
    public int? RssiDbm { get; set; }
    /// <summary>% RAM free (0..100).</summary>
    public decimal? FreeMemoryPercent { get; set; }
    /// <summary>Uptime device (giây) từ lần boot cuối.</summary>
    public long? UptimeSeconds { get; set; }
    /// <summary>Số reading đang queue tại device chưa upload.</summary>
    public int? QueuedReadingCount { get; set; }
    /// <summary>Timestamp ghi nhận tại device (UTC) — backend check clock skew.</summary>
    public DateTime DeviceTimestamp { get; set; }

    // Sprint IoT-2 #IoT2-10 — ESP32 field mapping per overall.md §52.2/§52.4.
    /// <summary>CPU usage 0..100 — ESP32 không tính được sẽ gửi null.</summary>
    public decimal? Cpu { get; set; }

    /// <summary>Free disk MB — ESP32 không có disk → null. Để khớp interface với gateway-class device.</summary>
    public long? DiskFreeMb { get; set; }

    /// <summary>Nhiệt độ MCU (°C). Alias của RssiDbm/FreeMemory cho ESP32.</summary>
    public decimal? Temperature { get; set; }

    /// <summary>Memory usage (MB) — alias chuẩn ESP32 thay vì percent.</summary>
    public long? MemoryUsageMb { get; set; }

    /// <summary>RSSI WiFi (dBm) — alias rõ nghĩa của <see cref="RssiDbm"/> theo §52.2.</summary>
    public int? SignalStrengthDbm
    {
        get => RssiDbm;
        set { if (value.HasValue) RssiDbm = value; }
    }

    /// <summary>Số reading đang queue trong NVS (alias rõ nghĩa của <see cref="QueuedReadingCount"/>).</summary>
    public int? LocalQueueDepth
    {
        get => QueuedReadingCount;
        set { if (value.HasValue) QueuedReadingCount = value; }
    }
}

/// <summary>Device cập nhật progress OTA.</summary>
public class UpdateIotFirmwareUpdateLogCommand : IRequest<CommonResponse<object>>
{
    /// <summary>Lấy từ route — body không bind.</summary>
    [JsonIgnore]
    [BindNever]
    public Guid LogId { get; set; }

    /// <summary>Lấy từ claim — body không bind.</summary>
    [JsonIgnore]
    [BindNever]
    public Guid DeviceId { get; set; }

    /// <summary>Filter theo status enum.</summary>
    public IotFirmwareUpdateStatusEnum Status { get; set; }
    /// <summary>Số byte device đã download (firmware OTA).</summary>
    public long? BytesDownloaded { get; set; }
    /// <summary>Lý do thất bại (nếu Status=Failed/RolledBack).</summary>
    public string? FailureReason { get; set; }
}
