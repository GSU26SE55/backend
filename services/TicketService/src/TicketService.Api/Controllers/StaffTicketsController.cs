using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.CQRS.Query.Ticket;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Api.Controllers;

/// <summary>
/// Controller dành riêng cho Nhân viên kỹ thuật (Staff) xử lý Ticket được giao.
/// </summary>
[ApiController]
[Route("api/staff/tickets")]
[Authorize(Roles = "Staff")]
[Produces("application/json")]
public class StaffTicketsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITicketCurrentUserService _currentUser;

    public StaffTicketsController(IMediator mediator, ITicketCurrentUserService currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Nhân viên kỹ thuật lấy danh sách ticket được giao cho chính mình.
    /// </summary>
    /// <remarks>
    /// Hệ thống tự động lọc theo ID nhân viên gán (AssignedStaffId) từ Token.
    /// </remarks>
    /// <param name="query">Tiêu chí lọc và phân trang.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Lấy danh sách thành công.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    [HttpGet("me")]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<PaginationResponse<TicketDTO>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyTickets([FromQuery] MyTicketsAsStaffQuery query, CancellationToken ct)
    {
        var result = await _mediator.Send(query, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Snapshot KPI dashboard cho chính Staff đang đăng nhập (open/resolved, SLA risk near-breach ≤25%/breached/paused, count theo status, trend 7 ngày).
    /// </summary>
    /// <remarks>
    /// Scope theo AssignedStaffId từ Token — thay cho việc FE tự đếm trên 1 trang list (cap 100).
    /// SLA risk is monitored only for non-Urgent <c>InProgress</c> tickets with a timer.
    /// Snapshot hiện tại — KHÔNG nhận from/to. FE nên cache ~1 phút (staleTime).
    /// </remarks>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Trả thống kê thành công.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    [HttpGet("dashboard/stats")]
    [ProducesResponseType(typeof(CommonResponse<StaffTicketDashboardStatsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMyDashboardStats(CancellationToken ct)
    {
        var result = await _mediator.Send(new MyTicketDashboardStatsAsStaffQuery(), ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Primary Staff holds active work with a reason, note and future appointment.
    /// </summary>
    /// <remarks>
    /// Moves <c>InProgress</c> to <c>Pending/Held</c> and pauses SLA.
    /// </remarks>
    /// <param name="id">ID của Ticket.</param>
    /// <param name="command">Lý do và ghi chú.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Tạm dừng thành công.</response>
    [HttpPost("{id}/hold")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Hold(Guid id, [FromBody] TicketHoldCommand command, CancellationToken ct)
    {
        command.TicketId = id;
        command.StaffId = string.IsNullOrEmpty(_currentUser.UserId) ? Guid.Empty : Guid.Parse(_currentUser.UserId);
        command.StaffName = _currentUser.FullName!;

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Primary Staff resumes a held ticket early with a required reason.
    /// </summary>
    /// <remarks>
    /// Trạng thái quay lại <c>InProgress</c>, tiếp tục tính SLA.
    /// </remarks>
    /// <param name="id">ID của Ticket.</param>
    /// <param name="command">Early-resume reason.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Tiếp tục thành công.</response>
    [HttpPost("{id}/resume")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Resume(Guid id, [FromBody] TicketResumeCommand command, CancellationToken ct)
    {
        command.TicketId = id;
        command.StaffId = string.IsNullOrEmpty(_currentUser.UserId) ? Guid.Empty : Guid.Parse(_currentUser.UserId);
        command.StaffName = _currentUser.FullName!;

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Staff báo cáo đã hoàn thành việc giải quyết sự cố/yêu cầu.
    /// </summary>
    /// <remarks>
    /// - Chuyển trạng thái sang <c>Completed</c>.
    /// - Chờ Manager phê duyệt kết quả.
    /// </remarks>
    /// <param name="id">ID của Ticket.</param>
    /// <param name="command">Tổng kết xử lý.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Báo cáo thành công.</response>
    /// <response code="403">Không đủ thẩm quyền (với ticket đã Escalated).</response>
    [HttpPost("{id}/resolve")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Resolve(Guid id, [FromBody] TicketResolveCommand command, CancellationToken ct)
    {
        command.TicketId = id;
        command.StaffId = string.IsNullOrEmpty(_currentUser.UserId) ? Guid.Empty : Guid.Parse(_currentUser.UserId);
        command.StaffName = _currentUser.FullName!;

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Staff chủ động yêu cầu chuyển cấp xử lý (Escalation) khi vượt quá khả năng.
    /// </summary>
    /// <remarks>
    /// Lý do: SkillGap, OutOfResources, ComplexIssue. Chờ Manager điều phối.
    /// </remarks>
    /// <param name="id">ID của Ticket.</param>
    /// <param name="command">Lý do yêu cầu.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Gửi yêu cầu thành công.</response>
    [HttpPost("{id}/escalate-request")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> EscalateRequest(Guid id, [FromBody] TicketEscalateRequestCommand command,
        CancellationToken ct)
    {
        command.TicketId = id;
        command.StaffId = string.IsNullOrEmpty(_currentUser.UserId) ? Guid.Empty : Guid.Parse(_currentUser.UserId);
        command.StaffName = _currentUser.FullName!;

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }
}
