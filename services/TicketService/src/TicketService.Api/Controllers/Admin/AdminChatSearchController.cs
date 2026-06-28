using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.Chats;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Api.Controllers.Admin;

/// <summary>
/// Global chat search across all tickets — dành cho Manager/Admin.
/// </summary>
[ApiController]
[Route("api/chats")]
[Authorize(Roles = "Manager,Admin")]
[Produces("application/json")]
public class AdminChatSearchController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITicketCurrentUserService _currentUser;

    public AdminChatSearchController(IMediator mediator, ITicketCurrentUserService currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Tìm kiếm chat toàn hệ thống theo keyword, customer, ngày tháng.
    /// </summary>
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] ChatGlobalSearchQuery query)
    {
        query.ActorUserId = Guid.TryParse(_currentUser.UserId, out var uid) ? uid : Guid.Empty;
        query.ActorRoles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();
        return Ok(await _mediator.Send(query));
    }
}
