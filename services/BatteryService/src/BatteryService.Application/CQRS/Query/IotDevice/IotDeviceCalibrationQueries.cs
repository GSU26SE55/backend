using System.Text.Json.Serialization;
using BatteryService.Application.DTOs;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.IotDevice;

/// <summary>Sprint IoT-2 #IoT2-32 — list calibration của 1 device.</summary>
public class GetIotDeviceCalibrationsQuery : IRequest<CommonResponse<List<IotDeviceCalibrationDto>>>
{
    /// <summary>Lấy từ route — query string KHÔNG được override (BindNever) + body KHÔNG bind (JsonIgnore).</summary>
    [JsonIgnore]
    [BindNever]
    public Guid IotDeviceId { get; set; }

    /// <summary>Channel sensor (vd "voltage", "current", "temperature").</summary>
    public string? Channel { get; set; }
    /// <summary>Bao gồm calibration đã expired.</summary>
    public bool IncludeExpired { get; set; }
}

/// <summary>Sprint IoT-2 #IoT2-32 — calibration sắp expire trong N ngày (default 30).</summary>
public class GetIotCalibrationsExpiringQuery : IRequest<CommonResponse<List<IotDeviceCalibrationDto>>>
{
    /// <summary>Filter calibration expire trong N ngày tới (default 30).</summary>
    public int WithinDays { get; set; } = 30;
}
