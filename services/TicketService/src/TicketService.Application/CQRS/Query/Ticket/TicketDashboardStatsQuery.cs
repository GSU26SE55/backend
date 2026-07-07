using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.Tickets;

namespace TicketService.Application.CQRS.Query.Ticket;

/// <summary>
/// Snapshot KPI ticket toàn hệ thống cho Dashboard Admin/Manager — không nhận tham số (snapshot hiện tại).
/// </summary>
public class TicketDashboardStatsQuery : IRequest<CommonResponse<TicketDashboardStatsDto>>
{
}
