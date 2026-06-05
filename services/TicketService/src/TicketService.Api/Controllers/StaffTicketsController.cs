using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.CQRS.Query.Ticket;
using TicketService.Application.DTOs.Response.Ticket;

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

    public StaffTicketsController(IMediator mediator)
    {
        _mediator = mediator;
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
    [ProducesResponseType(typeof(CommonResponse<PaginationResponse<TicketDTO>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyTickets([FromQuery] MyTicketsAsStaffQuery query, CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue)
            return Unauthorized();

        query.ActorStaffId = actorId.Value;
        var result = await _mediator.Send(query, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Staff xác nhận bắt đầu xử lý ticket đã được giao.
    /// </summary>
    /// <remarks>
    /// - Ticket phải ở trạng thái <c>Assigned</c>.
    /// - Chuyển trạng thái sang <c>InProgress</c>.
    /// </remarks>
    /// <param name="id">ID của Ticket.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Bắt đầu thành công.</response>
    /// <response code="403">Sai trạng thái hoặc không có quyền.</response>
    /// <response code="404">Không tìm thấy ticket.</response>
    [HttpPost("{id}/start")]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Start(Guid id, CancellationToken ct)
    {
        var command = new TicketStartCommand
        {
            TicketId = id,
            StaffId = GetUserId(),
            StaffName = GetUserName()
        };

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Staff tạm dừng xử lý ticket vì lý do khách quan.
    /// </summary>
    /// <remarks>
    /// Tạm dừng tính SLA. Các lý do: WaitingCustomer, WaitingParts, WaitingOnsiteSchedule.
    /// </remarks>
    /// <param name="id">ID của Ticket.</param>
    /// <param name="command">Lý do và ghi chú.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Tạm dừng thành công.</response>
    [HttpPost("{id}/hold")]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Hold(Guid id, [FromBody] TicketHoldCommand command, CancellationToken ct)
    {
        command.TicketId = id;
        command.StaffId = GetUserId();
        command.StaffName = GetUserName();

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Staff tiếp tục xử lý ticket từ trạng thái tạm dừng.
    /// </summary>
    /// <remarks>
    /// Trạng thái quay lại <c>InProgress</c>, tiếp tục tính SLA.
    /// </remarks>
    /// <param name="id">ID của Ticket.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Tiếp tục thành công.</response>
    [HttpPost("{id}/resume")]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Resume(Guid id, CancellationToken ct)
    {
        var command = new TicketResumeCommand
        {
            TicketId = id,
            StaffId = GetUserId(),
            StaffName = GetUserName()
        };

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Staff báo cáo đã hoàn thành việc giải quyết sự cố/yêu cầu.
    /// </summary>
    /// <remarks>
    /// - Chuyển trạng thái sang <c>Resolved</c>.
    /// - Chờ Manager phê duyệt kết quả.
    /// </remarks>
    /// <param name="id">ID của Ticket.</param>
    /// <param name="command">Tổng kết xử lý.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Báo cáo thành công.</response>
    /// <response code="403">Không đủ thẩm quyền (với ticket đã Escalated).</response>
    [HttpPost("{id}/resolve")]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Resolve(Guid id, [FromBody] TicketResolveCommand command, CancellationToken ct)
    {
        command.TicketId = id;
        command.StaffId = GetUserId();
        command.StaffName = GetUserName();

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
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> EscalateRequest(Guid id, [FromBody] TicketEscalateRequestCommand command,
        CancellationToken ct)
    {
        command.TicketId = id;
        command.StaffId = GetUserId();
        command.StaffName = GetUserName();

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    private Guid GetUserId()
    {
        var userIdClaim = User.FindFirst("id")?.Value;
        Guid.TryParse(userIdClaim, out var userId);
        return userId;
    }

    private string GetUserName()
    {
        return User.FindFirst("name")?.Value
               ?? User.FindFirst(ClaimTypes.Name)?.Value
               ?? "Unknown";
    }

    private Guid? GetCurrentUserId()
    {
        var raw = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var actorId) ? actorId : null;
    }
}
