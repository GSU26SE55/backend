using BatteryService.Application.DTOs;
using BatteryService.Domain.Enums;
using MediatR;
using SharedContracts.Common.Requests;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.BatteryAsset;

public class GetBatteryAssetsQuery : PaginationRequest, IRequest<CommonResponse<PaginationResponse<BatteryAssetDto>>>
{
    /// <summary>Từ khoá search (case-insensitive).</summary>
    public string? Keyword { get; set; }

    /// <summary>ID Customer (Guid).</summary>
    public Guid? CustomerId { get; set; }

    /// <summary>ID BatteryType (Guid).</summary>
    public Guid? BatteryTypeId { get; set; }

    /// <summary>ID Site (Guid).</summary>
    public Guid? SiteId { get; set; }

    /// <summary>Filter theo status enum.</summary>
    public BatteryStatusEnum? Status { get; set; }

    /// <summary>Bao gồm soft-deleted records.</summary>
    public bool IncludeDeleted { get; set; }

    /// <summary>
    /// Cột sort. Whitelist: serialNumber | batteryTypeName | customerName | siteName | status | installDate.
    /// Giá trị ngoài whitelist → createdAt (mặc định).
    /// </summary>
    public string? SortBy { get; set; }

    /// <summary>Hướng sort: asc | desc. Mặc định desc.</summary>
    public string? SortDir { get; set; }
}
