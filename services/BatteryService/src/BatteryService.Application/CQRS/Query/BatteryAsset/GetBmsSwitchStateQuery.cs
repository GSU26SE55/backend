using BatteryService.Application.DTOs;
using MediatR;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.BatteryAsset;

public class GetBmsSwitchStateQuery : IRequest<CommonResponse<BmsSwitchStateDto>>
{
    public Guid BatteryAssetId { get; set; }
}
