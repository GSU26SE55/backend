using BatteryService.Application.DTOs;
using MediatR;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.Dashboard;

public class GetBatteryDashboardStatsQuery : IRequest<CommonResponse<BatteryDashboardStatsDto>>
{
    /// <summary>Tùy chọn lọc theo Site (Admin xem all, Manager scope theo site).</summary>
    public Guid? SiteId { get; set; }
}
