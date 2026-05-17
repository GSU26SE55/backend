using BatteryService.Application.DTOs;
using MediatR;
using SharedContracts.Common.Requests;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.BatteryAsset;

public class GetMyBatteryAssetsQuery : PaginationRequest, IRequest<CommonResponse<PaginationResponse<BatteryAssetDto>>>
{
}
