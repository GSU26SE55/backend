using BatteryService.Application.DTOs;
using BatteryService.Domain.Enums;
using MediatR;
using SharedContracts.Common.Requests;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.Site;

public class GetSitesQuery : PaginationRequest, IRequest<CommonResponse<PaginationResponse<SiteDto>>>
{
    public string? Keyword { get; set; }

    public Guid? CustomerId { get; set; }

    public SiteStatusEnum? Status { get; set; }

    public bool IncludeDeleted { get; set; }
}
