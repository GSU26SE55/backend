using BatteryService.Application.DTOs;
using MediatR;
using SharedContracts.Common.Requests;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.Site;

public class GetMySitesQuery : PaginationRequest, IRequest<CommonResponse<PaginationResponse<SiteDto>>>
{
}
