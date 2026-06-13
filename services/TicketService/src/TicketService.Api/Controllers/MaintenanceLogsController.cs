using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.MaintenanceLogAdd;
using TicketService.Application.CQRS.Command.MaintenanceLogUpdate;
using TicketService.Application.CQRS.Query.MaintenanceLogs;
using TicketService.Application.DTOs.Response.Maintenance;
using TicketService.Application.DTOs.Response.Ticket;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize(Roles = "Staff,Manager,Admin")]
[Produces("application/json")]
public class MaintenanceLogsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITicketCurrentUserService _currentUser;

    public MaintenanceLogsController(IMediator mediator, ITicketCurrentUserService currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Lấy danh sách nhật ký bảo trì của chính Staff đang đăng nhập (Gom nhóm theo Ticket).
    /// </summary>
    /// <response code="200">Thành công.</response>
    [HttpGet("staff/tickets/maintenance-logs/me")]
    [Authorize(Roles = "Staff")]
    [ProducesResponseType(typeof(CommonResponse<List<StaffMaintenanceLogGroupDTO>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyLogs(CancellationToken ct)
    {
        var query = new MyMaintenanceLogsQuery(GetCurrentUserId());
        var result = await _mediator.Send(query, ct);
        return Ok(new CommonResponse<List<StaffMaintenanceLogGroupDTO>>
        {
            IsSuccess = true,
            StatusCode = StatusCodes.Status200OK,
            Message = "Lấy danh sách nhật ký bảo trì thành công.",
            Data = result
        });
    }

    /// <summary>
    /// Lấy danh sách nhật ký bảo trì của một Ticket cụ thể (Chỉ dành cho Manager/Admin).
    /// </summary>
    /// <param name="ticketId">ID của Ticket.</param>
    /// <response code="200">Thành công.</response>
    [HttpGet("tickets/{ticketId:guid}/maintenance-logs")]
    [Authorize(Roles = "Manager,Admin")]
    [ProducesResponseType(typeof(CommonResponse<List<MaintenanceLogDTO>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLogsByTicket(Guid ticketId, CancellationToken ct)
    {
        var query = new MaintenanceLogsByTicketQuery(ticketId);
        var result = await _mediator.Send(query, ct);
        return Ok(new CommonResponse<List<MaintenanceLogDTO>>
        {
            IsSuccess = true,
            StatusCode = StatusCodes.Status200OK,
            Message = "Lấy nhật ký bảo trì theo ticket thành công.",
            Data = result
        });
    }

    /// <summary>
    /// Thêm nhật ký bảo trì cho Ticket.
    /// </summary>
    /// <remarks>
    /// Dành cho Staff ghi lại quá trình sửa chữa, bảo trì thiết bị.
    ///
    /// Cách hoạt động:
    /// - Staff phải đang assigned vào ticket.
    /// - Ticket phải đang ở trạng thái có thể thêm log (KHÔNG phải <c>PendingApproval</c>/<c>Resolved</c>/<c>Closed</c>).
    /// - Nếu <c>CompletedAt = null</c> (log đang mở), ticket KHÔNG được có log nào khác đang mở — phải đóng log cũ trước.
    /// </remarks>
    /// <param name="ticketId">Id của ticket.</param>
    /// <param name="command">Nội dung nhật ký bảo trì (LogType, Description, …).</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="201">Tạo log thành công, trả về <c>MaintenanceLogId</c>.</response>
    /// <response code="400">Validation lỗi field (Description rỗng, LogType không hợp lệ…).</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Ticket đã ở trạng thái không cho phép thêm log (chờ phê duyệt / đã hoàn thành) — Staff không có quyền tác động.</response>
    /// <response code="404">Không tìm thấy Ticket.</response>
    /// <response code="409">Đã có một nhật ký bảo trì khác chưa hoàn thành cho ticket này (state conflict — phải đóng log cũ trước khi mở log mới).</response>
    [HttpPost("tickets/{ticketId:guid}/maintenance-logs")]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddLog(Guid ticketId, [FromBody] MaintenanceLogAddCommand command, CancellationToken ct)
    {
        command.TicketId = ticketId;
        command.StaffId = GetCurrentUserId();

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Cập nhật nhật ký bảo trì (Partial Update).
    /// </summary>
    [HttpPatch("tickets/{ticketId:guid}/maintenance-logs/{logId:guid}")]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateLog(Guid ticketId, Guid logId, [FromBody] MaintenanceLogUpdateCommand command, CancellationToken ct)
    {
        command.LogId = logId;
        command.StaffId = GetCurrentUserId();

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    private Guid GetCurrentUserId()
    {
        return string.IsNullOrEmpty(_currentUser.UserId) ? Guid.Empty : Guid.Parse(_currentUser.UserId);
    }
}
