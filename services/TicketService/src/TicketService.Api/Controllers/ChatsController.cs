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

    private Guid? GetCurrentUserId()
    {
        var raw = _currentUser.UserId;
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    private string[] GetCurrentRoles()
        => User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();
}
