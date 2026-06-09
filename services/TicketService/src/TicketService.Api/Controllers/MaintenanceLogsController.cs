using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
    [ProducesResponseType(typeof(List<StaffMaintenanceLogGroupDTO>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyLogs(CancellationToken ct)
    {
        var query = new MyMaintenanceLogsQuery(GetCurrentUserId());
        var result = await _mediator.Send(query, ct);
        return Ok(result);
    }

    /// <summary>
    /// Lấy danh sách nhật ký bảo trì của một Ticket cụ thể (Chỉ dành cho Manager/Admin).
    /// </summary>
    /// <param name="ticketId">ID của Ticket.</param>
    /// <response code="200">Thành công.</response>
    [HttpGet("tickets/{ticketId:guid}/maintenance-logs")]
    [Authorize(Roles = "Manager,Admin")]
    [ProducesResponseType(typeof(List<MaintenanceLogDTO>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLogsByTicket(Guid ticketId, CancellationToken ct)
    {
        var query = new MaintenanceLogsByTicketQuery(ticketId);
        var result = await _mediator.Send(query, ct);
        return Ok(result);
    }

    /// <summary>
    /// Thêm nhật ký bảo trì cho Ticket.
    /// </summary>
    /// <remarks>
    /// Dành cho Staff ghi lại quá trình sửa chữa, bảo trì thiết bị.
    /// </remarks>
    [HttpPost("tickets/{ticketId:guid}/maintenance-logs")]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status201Created)]
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
