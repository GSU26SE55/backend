using BatteryService.Application.DTOs;
using MediatR;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.Site;

public class GetSiteDashboardQuery : IRequest<CommonResponse<SiteDashboardDto>>
{
    /// <summary>Định danh resource.</summary>
    public Guid Id { get; set; }
}
