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
