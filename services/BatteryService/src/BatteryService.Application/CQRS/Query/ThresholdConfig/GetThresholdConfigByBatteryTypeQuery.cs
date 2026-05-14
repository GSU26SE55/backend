using BatteryService.Application.DTOs;
using MediatR;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.ThresholdConfig;

public class GetThresholdConfigByBatteryTypeQuery : IRequest<CommonResponse<ThresholdConfigDto>>
{
    public Guid BatteryTypeId { get; set; }

    public bool IncludeInactive { get; set; }
}
