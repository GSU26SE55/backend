using BatteryService.Application.DTOs;
using MediatR;
using SharedContracts.Common.Requests;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.BatteryGroup;

public class GetBatteryGroupsQuery : PaginationRequest, IRequest<CommonResponse<PaginationResponse<BatteryGroupDto>>>
{
    public string? Keyword { get; set; }

    public Guid? SiteId { get; set; }

    public Guid? BatteryTypeId { get; set; }

    public bool IncludeDeleted { get; set; }
}
