using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketService.Application.CQRS.Command.CommentAdd;
using TicketService.Application.DTOs.Response.Ticket;
using TicketService.Domain.Enums;

namespace TicketService.Api.Controllers;

[ApiController]
[Route("api/tickets/{ticketId}/comments")]
[Authorize]
[Produces("application/json")]
public class TicketCommentsController : ControllerBase
{
    private readonly IMediator _mediator;

    public TicketCommentsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Thêm bình luận vào Ticket.
    /// </summary>
    /// <remarks>
    /// Áp dụng cho cả Customer và Staff.
    /// - <c>IsInternal</c>: Nếu true, chỉ Staff/Manager mới có thể xem (ẩn với Customer).
    /// - <c>Attachments</c>: Danh sách tệp đính kèm (nếu có).
    /// </remarks>
    /// <param name="ticketId">ID của Ticket.</param>
    /// <param name="command">Nội dung bình luận và đính kèm.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="201">Thêm bình luận thành công.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="404">Không tìm thấy ticket.</response>
    [HttpPost]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddComment(Guid ticketId, [FromBody] CommentAddCommand command, CancellationToken ct)
    {
        command.TicketId = ticketId;
        command.UserId = GetUserId();
        command.UserDisplayName = GetUserName();

        var roles = GetCurrentRoles();
        var userRole = ActorRoleEnum.Staff; // Default
        if (roles.Contains("Customer"))
            userRole = ActorRoleEnum.Customer;
        else if (roles.Contains("Manager"))
            userRole = ActorRoleEnum.Manager;
        else if (roles.Contains("Admin"))
            userRole = ActorRoleEnum.Admin;

        command.UserRole = userRole;

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

    private string[] GetCurrentRoles()
        => User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();
}
