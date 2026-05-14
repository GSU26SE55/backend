using BatteryService.Application.DTOs;
using MediatR;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.BatteryAsset;

public class GetBatteryAssetRealtimeQuery : IRequest<CommonResponse<BatteryAssetRealtimeDto>>
{
    public Guid Id { get; set; }
}
