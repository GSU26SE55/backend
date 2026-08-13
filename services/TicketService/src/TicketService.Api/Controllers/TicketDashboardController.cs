using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.Ticket;
using TicketService.Application.DTOs.Response.Tickets;

namespace TicketService.Api.Controllers;

/// <summary>
/// Endpoint thống kê snapshot cho Ticket dashboard Admin/Manager —
/// cùng pattern với <c>api/battery/dashboard/stats</c> bên BatteryService.
/// </summary>
[ApiController]
[Route("api/tickets/dashboard")]
[Authorize(Roles = "Manager,Admin")]
[Produces("application/json")]
public class TicketDashboardController : ControllerBase
{
    private readonly IMediator _mediator;

    public TicketDashboardController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Snapshot KPI ticket toàn hệ thống (total/open, SLA met-breached-running-paused + compliance, count theo status/priority, trend tạo mới 7 ngày, workload theo staff) — UI Dashboard Admin/Manager.
    /// </summary>
    /// <remarks>
    /// Thay cho việc FE tự đếm trên 1 trang list (bị cap theo pageSize):
    /// <list type="bullet">
    ///   <item><description><c>Total</c> / <c>OpenCount</c> — open = Open/Pending/InProgress/Request/ReAssign.</description></item>
    ///   <item><description><c>Sla</c> — count theo SlaTimerStatus + <c>CompliancePercent</c> = Met/(Met+Breached).</description></item>
    ///   <item><description><c>CountByStatus</c> — đủ 14 status (zero-fill) để FE tự nhóm pipeline, không gộp ClosedRejected vào "Hoàn tất".</description></item>
    ///   <item><description><c>CreatedTrend7Days</c> — bucket theo ngày UTC, ngày trống = 0.</description></item>
    ///   <item><description><c>OpenCountByStaff</c> — widget "Tải nhân sự" của Manager.</description></item>
    /// </list>
    ///
    /// Snapshot hiện tại — KHÔNG nhận from/to (báo cáo time-series dùng <c>/api/reports/*</c>).
    /// FE nên cache ~1 phút (staleTime).
    /// </remarks>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Trả thống kê thành công.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Manager/Admin.</response>
    [HttpGet("stats")]
    [ProducesResponseType(typeof(CommonResponse<TicketDashboardStatsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Stats(CancellationToken ct)
    {
        var result = await _mediator.Send(new TicketDashboardStatsQuery(), ct);
        return StatusCode(result.StatusCode, result);
    }
}
