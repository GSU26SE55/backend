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

    public string FirmwareVersion { get; set; } = string.Empty;
    public string? HardwareRevision { get; set; }
    public DateTime DeviceTimestamp { get; set; }
}

/// <summary>Heartbeat — telemetry health + tiến độ queue + clock skew.</summary>
public class IotDeviceHeartbeatCommand : IRequest<CommonResponse<IotHeartbeatAckDto>>
{
    [JsonIgnore]
    [BindNever]
    public Guid DeviceId { get; set; }

    [JsonIgnore]
    [BindNever]
    public string DeviceCode { get; set; } = string.Empty;

    public string? FirmwareVersion { get; set; }
    public int? RssiDbm { get; set; }
    public decimal? FreeMemoryPercent { get; set; }
    public long? UptimeSeconds { get; set; }
    public int? QueuedReadingCount { get; set; }
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

    public IotFirmwareUpdateStatusEnum Status { get; set; }
    public long? BytesDownloaded { get; set; }
    public string? FailureReason { get; set; }
}
