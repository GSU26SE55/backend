using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketService.Application.CQRS.Command.MaintenanceLogAdd;
using TicketService.Application.DTOs.Response.Ticket;

namespace TicketService.Api.Controllers;

[ApiController]
[Route("api/tickets/{ticketId}/maintenance-logs")]
[Authorize(Roles = "Staff,Manager,Admin")]
[Produces("application/json")]
public class MaintenanceLogsController : ControllerBase
{
    private readonly IMediator _mediator;

    public MaintenanceLogsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Thêm nhật ký bảo trì cho Ticket.
    /// </summary>
    /// <remarks>
    /// Dành cho Staff ghi lại quá trình sửa chữa, bảo trì thiết bị.
    /// Thông tin bao gồm: Loại log, nội dung xử lý, thời gian thực hiện, linh kiện sử dụng, ảnh chụp và tọa độ Check-in.
    /// </remarks>
    /// <param name="ticketId">ID của Ticket.</param>
    /// <param name="command">Thông tin nhật ký bảo trì.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="201">Thêm nhật ký thành công.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có quyền Staff/Manager.</response>
    /// <response code="404">Không tìm thấy ticket.</response>
    [HttpPost]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddLog(Guid ticketId, [FromBody] MaintenanceLogAddCommand command, CancellationToken ct)
    {
        command.TicketId = ticketId;
        command.StaffId = GetUserId();

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    private Guid GetUserId()
    {
        var userIdClaim = User.FindFirst("id")?.Value;
        Guid.TryParse(userIdClaim, out var userId);
        return userId;
    }
}
