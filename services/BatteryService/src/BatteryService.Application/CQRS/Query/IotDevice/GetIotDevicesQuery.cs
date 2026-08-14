using System.Text.Json.Serialization;
using BatteryService.Application.DTOs;
using BatteryService.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Requests;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.IotDevice;

public class GetIotDevicesQuery : PaginationRequest, IRequest<CommonResponse<PaginationResponse<IotDeviceDto>>>
{
    /// <summary>ID Site (Guid).</summary>
    public Guid? SiteId { get; set; }
    /// <summary>Filter theo status enum.</summary>
    public IotDeviceStatusEnum? Status { get; set; }
    /// <summary>Từ khoá search (case-insensitive).</summary>
    public string? Keyword { get; set; }
    /// <summary>Sort giảm dần theo CreatedAt nếu true.</summary>
    public bool IsDescending { get; set; } = true;

    /// <summary>
    /// Cột sort. Whitelist: deviceCode | displayName | siteName | status | currentFirmwareVersion | lastSeenAt.
    /// Giá trị ngoài whitelist → createdAt (mặc định).
    /// </summary>
    public string? SortBy { get; set; }

    /// <summary>Hướng sort: asc | desc. Nếu set sẽ ghi đè <see cref="IsDescending"/>.</summary>
    public string? SortDir { get; set; }
}

public class GetIotDeviceByIdQuery : IRequest<CommonResponse<IotDeviceDetailDto>>
{
    /// <summary>Lấy từ route — query string + body không bind để tránh nhầm lẫn nguồn.</summary>
    [JsonIgnore]
    [BindNever]
    public Guid Id { get; set; }
}

/// <summary>
/// Tra cứu IoT device theo <c>DeviceCode</c> (mã in trên thân thiết bị, vd "ESP32-SIM-001").
/// Dùng cho Staff/Manager resolve <c>deviceCode → device.Id (GUID)</c> trước khi gọi calibration API
/// (các route calibration keyed theo GUID — Staff không có nguồn GUID nào khác).
/// </summary>
public class GetIotDeviceByCodeQuery : IRequest<CommonResponse<IotDeviceDto>>
{
    /// <summary>Lấy từ route — query string + body không bind để tránh nhầm lẫn nguồn.</summary>
    [JsonIgnore]
    [BindNever]
    public string DeviceCode { get; set; } = string.Empty;
}
