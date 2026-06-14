using BatteryService.Application.DTOs;
using MediatR;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.BatteryAsset;

public class GetBatteryAssetRealtimeQuery : IRequest<CommonResponse<BatteryAssetRealtimeDto>>
{
    /// <summary>Định danh resource.</summary>
    public Guid Id { get; set; }
}
