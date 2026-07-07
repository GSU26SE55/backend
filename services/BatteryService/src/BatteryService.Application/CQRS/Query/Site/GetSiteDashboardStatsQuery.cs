using BatteryService.Application.DTOs;
using MediatR;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.Site;

/// <summary>
/// Snapshot tổng hợp toàn bộ Site cho Dashboard Admin/Manager — không nhận tham số (snapshot hiện tại).
/// </summary>
public class GetSiteDashboardStatsQuery : IRequest<CommonResponse<SiteDashboardStatsDto>>
{
}
