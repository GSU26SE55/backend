using BatteryService.Application.DTOs;
using MediatR;
using SharedContracts.Common.Requests;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.BatteryType;

public class GetBatteryTypesQuery : PaginationRequest, IRequest<CommonResponse<PaginationResponse<BatteryTypeDto>>>
{
    public string? Keyword { get; set; }

    public bool IncludeDeleted { get; set; }
}
