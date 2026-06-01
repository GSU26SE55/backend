using BatteryService.Application.DTOs;
using BatteryService.Domain.Enums;
using MediatR;
using SharedContracts.Common.Requests;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.BatteryAsset;

public class GetBatteryAssetsQuery : PaginationRequest, IRequest<CommonResponse<PaginationResponse<BatteryAssetDto>>>
{
    public string? Keyword { get; set; }

    public Guid? CustomerId { get; set; }

    public Guid? BatteryTypeId { get; set; }

    public Guid? SiteId { get; set; }

    public BatteryStatusEnum? Status { get; set; }

    public bool IncludeDeleted { get; set; }
}
