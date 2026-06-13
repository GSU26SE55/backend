using System.Text.Json.Serialization;
using BatteryService.Application.DTOs;
using BatteryService.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.IotDevice;

public class GetIotDevicesQuery : IRequest<CommonResponse<PaginationResponse<IotDeviceDto>>>
{
    public Guid? SiteId { get; set; }
    public IotDeviceStatusEnum? Status { get; set; }
    public string? Keyword { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public bool IsDescending { get; set; } = true;
}

public class GetIotDeviceByIdQuery : IRequest<CommonResponse<IotDeviceDto>>
{
    /// <summary>Lấy từ route — query string + body không bind để tránh nhầm lẫn nguồn.</summary>
    [JsonIgnore]
    [BindNever]
    public Guid Id { get; set; }
}
