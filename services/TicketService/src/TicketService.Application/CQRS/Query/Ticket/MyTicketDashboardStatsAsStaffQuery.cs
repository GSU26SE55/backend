using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.Tickets;

namespace TicketService.Application.CQRS.Query.Ticket;

/// <summary>
/// Snapshot KPI ticket cho chính Staff đang đăng nhập — scope theo AssignedStaffId từ JWT, không nhận tham số.
/// </summary>
public class MyTicketDashboardStatsAsStaffQuery : IRequest<CommonResponse<StaffTicketDashboardStatsDto>>
{
}
