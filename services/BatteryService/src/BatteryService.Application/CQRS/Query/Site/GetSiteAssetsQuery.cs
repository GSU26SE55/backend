using System.Text.Json.Serialization;
using BatteryService.Application.DTOs;
using BatteryService.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Requests;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.Site;

public class GetSiteAssetsQuery : PaginationRequest, IRequest<CommonResponse<PaginationResponse<BatteryAssetDto>>>
{
    /// <summary>ID Site (Guid).</summary>
    [JsonIgnore]
    [BindNever]
    public Guid SiteId { get; set; }

    /// <summary>Filter theo status enum.</summary>
    public BatteryStatusEnum? Status { get; set; }

    /// <summary>
    /// Cột sort. Whitelist: serialNumber | batteryTypeName | status | installDate | lastSensorReadingAt.
    /// Giá trị ngoài whitelist → createdAt (mặc định).
    /// </summary>
    public string? SortBy { get; set; }

    /// <summary>Hướng sort: asc | desc. Mặc định desc.</summary>
    public string? SortDir { get; set; }
}
