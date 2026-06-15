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
}
