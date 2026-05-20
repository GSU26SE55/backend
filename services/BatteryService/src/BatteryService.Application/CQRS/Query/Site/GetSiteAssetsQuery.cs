using BatteryService.Application.DTOs;
using BatteryService.Domain.Enums;
using MediatR;
using SharedContracts.Common.Requests;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.Site;

public class GetSiteAssetsQuery : PaginationRequest, IRequest<CommonResponse<PaginationResponse<BatteryAssetDto>>>
{
    public Guid SiteId { get; set; }

    public BatteryStatusEnum? Status { get; set; }
}
