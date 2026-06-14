using System.Text.Json.Serialization;
using BatteryService.Application.DTOs;
using BatteryService.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.IotDevice;

public class GetIotDevicesQuery : IRequest<CommonResponse<PaginationResponse<IotDeviceDto>>>
{
    /// <summary>ID Site (Guid).</summary>
    public Guid? SiteId { get; set; }
    /// <summary>Filter theo status enum.</summary>
    public IotDeviceStatusEnum? Status { get; set; }
    /// <summary>Từ khoá search (case-insensitive).</summary>
    public string? Keyword { get; set; }
    /// <summary>Số trang (1-based).</summary>
    public int Page { get; set; } = 1;
    /// <summary>Số bản ghi mỗi trang (clamp [1, 100]).</summary>
    public int PageSize { get; set; } = 20;
    /// <summary>Sort giảm dần theo CreatedAt nếu true.</summary>
    public bool IsDescending { get; set; } = true;
}

public class GetIotDeviceByIdQuery : IRequest<CommonResponse<IotDeviceDto>>
{
    /// <summary>Lấy từ route — query string + body không bind để tránh nhầm lẫn nguồn.</summary>
    [JsonIgnore]
    [BindNever]
    public Guid Id { get; set; }
}
