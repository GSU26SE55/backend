using BatteryService.Application.DTOs;
using MediatR;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.ThresholdConfig;

public class GetThresholdConfigByBatteryTypeQuery : IRequest<CommonResponse<ThresholdConfigDto>>
{
    /// <summary>ID BatteryType (Guid).</summary>
    public Guid BatteryTypeId { get; set; }

    /// <summary>Bao gồm threshold inactive.</summary>
    public bool IncludeInactive { get; set; }
}
