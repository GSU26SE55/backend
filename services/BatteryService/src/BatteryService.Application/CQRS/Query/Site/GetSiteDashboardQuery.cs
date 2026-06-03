using BatteryService.Application.DTOs;
using MediatR;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.Site;

public class GetSiteDashboardQuery : IRequest<CommonResponse<SiteDashboardDto>>
{
    public Guid Id { get; set; }
}
