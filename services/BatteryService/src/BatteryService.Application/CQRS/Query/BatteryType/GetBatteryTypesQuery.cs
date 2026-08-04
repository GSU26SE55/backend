using BatteryService.Application.DTOs;
using MediatR;
using SharedContracts.Common.Requests;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.BatteryType;

public class GetBatteryTypesQuery : PaginationRequest, IRequest<CommonResponse<PaginationResponse<BatteryTypeDto>>>
{
    /// <summary>Từ khoá search (case-insensitive).</summary>
    public string? Keyword { get; set; }

    /// <summary>Bao gồm soft-deleted records.</summary>
    public bool IncludeDeleted { get; set; }

    /// <summary>
    /// Cột sort. Whitelist: name | manufacturer | chemistry | nominalCapacityAh | nominalVoltage | maxCycleCount.
    /// Giá trị ngoài whitelist → createdAt (mặc định).
    /// </summary>
    public string? SortBy { get; set; }

    /// <summary>Hướng sort: asc | desc. Mặc định desc.</summary>
    public string? SortDir { get; set; }
}
