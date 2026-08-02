using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketService.Application.CQRS.Query.Chats;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Api.Controllers;

[ApiController]
[Route("api/chats/mentions")]
[Authorize]
[Produces("application/json")]
public class ChatMentionsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITicketCurrentUserService _currentUser;

    public ChatMentionsController(IMediator mediator, ITicketCurrentUserService currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Lấy danh sách mention của user hiện tại trên mọi ticket.
    /// </summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(MyMentionsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyMentions(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_currentUser.UserId) || !Guid.TryParse(_currentUser.UserId, out var actorId))
        {
            return Unauthorized();
        }

        var result = await _mediator.Send(new MyMentionsQuery
        {
            ActorUserId = actorId,
            ActorRoles = string.IsNullOrWhiteSpace(_currentUser.Role) ? new List<string>() : new List<string> { _currentUser.Role },
            PageNumber = page,
            PageSize = pageSize
        }, ct);

        return StatusCode(result.StatusCode, result);
    }
}
