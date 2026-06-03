using BatteryService.Application.DTOs;
using MediatR;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.BatteryAsset;

public class GetBatteryAssetByIdQuery : IRequest<CommonResponse<BatteryAssetDto>>
{
    public Guid Id { get; set; }
}
