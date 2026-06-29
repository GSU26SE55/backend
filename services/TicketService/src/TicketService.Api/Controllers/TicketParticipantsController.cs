using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketService.Application.CQRS.Command.Participants;
using TicketService.Application.CQRS.Query.ParticipantHistory;
using TicketService.Application.CQRS.Query.TicketParticipants;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Enums;

namespace TicketService.Api.Controllers;

[ApiController]
[Route("api/tickets/{ticketId}/participants")]
[Authorize]
[Produces("application/json")]
public class TicketParticipantsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITicketCurrentUserService _currentUser;

    public TicketParticipantsController(IMediator mediator, ITicketCurrentUserService currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Thêm participant vào ticket — Manager/Admin hoặc PrimaryAssignee của ticket.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ParticipantActionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddParticipant(Guid ticketId, [FromBody] ParticipantAddCommand command, CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue)
            return Unauthorized();

        command.TicketId = ticketId;
        command.ActorUserId = actorId.Value;
        command.ActorRole = ResolveActorRole(_currentUser.Role);

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Xóa participant khỏi ticket — Manager/Admin. Xóa Owner chỉ Admin và bắt buộc RemoveReason.
    /// </summary>
    [HttpDelete("{userId}")]
    [ProducesResponseType(typeof(ParticipantActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveParticipant(Guid ticketId, Guid userId, [FromBody] ParticipantRemoveCommand? command, CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue)
            return Unauthorized();

        command ??= new ParticipantRemoveCommand();
        command.TicketId = ticketId;
        command.UserId = userId;
        command.ActorUserId = actorId.Value;
        command.ActorRole = ResolveActorRole(_currentUser.Role);

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Thêm nhiều participant cùng lúc (all-or-nothing) — chỉ Manager/Admin.
    /// </summary>
    [HttpPost("bulk")]
    [ProducesResponseType(typeof(ParticipantBulkActionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> BulkAddParticipants(Guid ticketId, [FromBody] ParticipantBulkAddCommand command, CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue)
            return Unauthorized();

        command.TicketId = ticketId;
        command.ActorUserId = actorId.Value;
        command.ActorRole = ResolveActorRole(_currentUser.Role);

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Tự rời khỏi ticket — chỉ Watcher.
    /// </summary>
    [HttpPost("leave")]
    [ProducesResponseType(typeof(ParticipantActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SelfLeave(Guid ticketId, [FromBody] ParticipantSelfLeaveCommand? command, CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue)
            return Unauthorized();

        command ??= new ParticipantSelfLeaveCommand();
        command.TicketId = ticketId;
        command.ActorUserId = actorId.Value;

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Cập nhật vai trò (ParticipantType/CanPost/CanViewInternal) của participant — chỉ Manager/Admin.
    /// </summary>
    [HttpPatch("{userId}")]
    [ProducesResponseType(typeof(ParticipantActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateParticipantRole(Guid ticketId, Guid userId, [FromBody] ParticipantUpdateRoleCommand command, CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue)
            return Unauthorized();

        command.TicketId = ticketId;
        command.UserId = userId;
        command.ActorUserId = actorId.Value;
        command.ActorRole = ResolveActorRole(_currentUser.Role);

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Lấy danh sách participant active của ticket — bất kỳ ai access được ticket.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(TicketParticipantsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetParticipants(Guid ticketId, CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue)
            return Unauthorized();

        var result = await _mediator.Send(new TicketParticipantsQuery
        {
            TicketId = ticketId,
            ActorUserId = actorId.Value,
            ActorRoles = GetCurrentRoles()
        }, ct);

        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Lấy toàn bộ lịch sử participant (bao gồm đã bị xóa) — chỉ Staff/Manager/Admin.
    /// </summary>
    [HttpGet("history")]
    [ProducesResponseType(typeof(ParticipantHistoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetParticipantHistory(Guid ticketId, CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue)
            return Unauthorized();

        var result = await _mediator.Send(new ParticipantHistoryQuery
        {
            TicketId = ticketId,
            ActorUserId = actorId.Value,
            ActorRoles = GetCurrentRoles()
        }, ct);

        return StatusCode(result.StatusCode, result);
    }

    private Guid? GetCurrentUserId()
    {
        var raw = _currentUser.UserId;
        return Guid.TryParse(raw, out var actorId) ? actorId : null;
    }

    private string[] GetCurrentRoles()
    {
        return User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();
    }

    private static ActorRoleEnum ResolveActorRole(string? roleStr)
    {
        return roleStr switch
        {
            "Customer" => ActorRoleEnum.Customer,
            "Manager" => ActorRoleEnum.Manager,
            "Admin" => ActorRoleEnum.Admin,
            _ => ActorRoleEnum.Staff
        };
    }
}
