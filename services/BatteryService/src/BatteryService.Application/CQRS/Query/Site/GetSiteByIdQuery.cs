using BatteryService.Application.DTOs;
using MediatR;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.Site;

public class GetSiteByIdQuery : IRequest<CommonResponse<SiteDto>>
{
    public Guid Id { get; set; }
}
