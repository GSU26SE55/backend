using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketService.Application.CQRS.Query.Chats;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Api.Controllers;

[ApiController]
[Route("api/chats")]
[Authorize]
[Produces("application/json")]
public class ChatsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITicketCurrentUserService _currentUser;

    public ChatsController(IMediator mediator, ITicketCurrentUserService currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Tổng số tin nhắn chưa đọc của user hiện tại trên toàn bộ hệ thống.
    /// </summary>
    [HttpGet("unread-count")]
    [ProducesResponseType(typeof(TicketUnreadCountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyUnreadCount(CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue)
            return Unauthorized();

        var result = await _mediator.Send(new MyUnreadCountQuery
        {
            ActorUserId = actorId.Value,
            ActorRoles = GetCurrentRoles()
        }, ct);

        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Số tin nhắn chưa đọc của user hiện tại, gom theo từng Customer.
    /// </summary>
    /// <remarks>
    /// Dùng cho màn "Khách hàng" của Staff: 1 call duy nhất thay vì gọi
    /// <c>/api/tickets/{id}/chats/unread-count</c> cho từng ticket.
    ///
    /// Đếm theo bản ghi TicketChat nên 1 tin nhắn kèm @mention vẫn tính 1 —
    /// mention là bảng con của chat, KHÔNG cộng thêm.
    ///
    /// Customer nào không có tin chưa đọc thì không xuất hiện trong list
    /// (client tự coi là 0).
    /// </remarks>
    [HttpGet("unread-count/by-customer")]
    [ProducesResponseType(typeof(UnreadCountByCustomerResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyUnreadCountByCustomer(CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue)
            return Unauthorized();

        var result = await _mediator.Send(new MyUnreadCountByCustomerQuery
        {
            ActorUserId = actorId.Value,
            ActorRoles = GetCurrentRoles()
        }, ct);

        return StatusCode(result.StatusCode, result);
    }

    private Guid? GetCurrentUserId()
    {
        var raw = _currentUser.UserId;
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    private string[] GetCurrentRoles()
        => User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();
}
