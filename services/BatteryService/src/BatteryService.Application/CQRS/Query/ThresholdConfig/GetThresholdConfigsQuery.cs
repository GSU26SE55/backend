using BatteryService.Application.DTOs;
using MediatR;
using SharedContracts.Common.Requests;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.ThresholdConfig;

public class GetThresholdConfigsQuery : PaginationRequest, IRequest<CommonResponse<PaginationResponse<ThresholdConfigDto>>>
{
    public Guid? BatteryTypeId { get; set; }

    public bool? IsActive { get; set; } = true;
}
