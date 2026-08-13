using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TicketService.Api.Extensions;
using TicketService.Application.CQRS.Command.Chats;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Enums;

namespace TicketService.Api.Controllers.Admin;

/// <summary>
/// Admin-only chat operations: override trên ticket đã Closed và khôi phục chat đã xóa.
/// </summary>
[ApiController]
[Route("api/admin/tickets/{ticketId}/chats")]
[Authorize(Roles = "Admin")]
[Produces("application/json")]
public class AdminTicketChatsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITicketCurrentUserService _currentUser;

    public AdminTicketChatsController(IMediator mediator, ITicketCurrentUserService currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Admin override — thêm bình luận dù ticket đang Closed (#517). Bắt buộc <c>OverrideReason</c>.
    /// </summary>
    /// <param name="ticketId">ID của Ticket.</param>
    /// <param name="command">Nội dung bình luận + lý do override.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="201">Thêm bình luận (override) thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ (thiếu OverrideReason).</response>
    /// <response code="403">Không phải Admin.</response>
    /// <response code="404">Không tìm thấy ticket.</response>
    [HttpPost("closed-override")]
    [EnableRateLimiting(ChatRateLimitingExtensions.ChatWritePolicy)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> OverrideAddChat(Guid ticketId, [FromBody] ChatOverrideAddCommand command, CancellationToken ct)
    {
        command.TicketId = ticketId;
        command.UserId = string.IsNullOrEmpty(_currentUser.UserId) ? Guid.Empty : Guid.Parse(_currentUser.UserId);
        command.UserDisplayName = _currentUser.FullName!;
        command.UserRole = ActorRoleEnum.Admin;

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Admin override — sửa bình luận dù ticket đang Closed (#517). Bắt buộc <c>OverrideReason</c>.
    /// </summary>
    /// <param name="ticketId">ID của Ticket.</param>
    /// <param name="id">ID của bình luận cần sửa.</param>
    /// <param name="command">Nội dung mới + lý do override.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Sửa bình luận (override) thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ (thiếu OverrideReason).</response>
    /// <response code="403">Không phải Admin.</response>
    /// <response code="404">Không tìm thấy ticket hoặc bình luận.</response>
    [HttpPut("{id}/closed-override")]
    [EnableRateLimiting(ChatRateLimitingExtensions.ChatWritePolicy)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> OverrideEditChat(Guid ticketId, Guid id, [FromBody] ChatOverrideEditCommand command, CancellationToken ct)
    {
        command.TicketId = ticketId;
        command.ChatId = id;
        command.UserId = string.IsNullOrEmpty(_currentUser.UserId) ? Guid.Empty : Guid.Parse(_currentUser.UserId);
        command.UserDisplayName = _currentUser.FullName!;
        command.UserRole = ActorRoleEnum.Admin;

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Admin override — xóa bình luận dù ticket đang Closed (#517). Bắt buộc <c>OverrideReason</c>.
    /// </summary>
    /// <param name="ticketId">ID của Ticket.</param>
    /// <param name="id">ID của bình luận cần xóa.</param>
    /// <param name="command">Lý do override.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Xóa bình luận (override) thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ (thiếu OverrideReason).</response>
    /// <response code="403">Không phải Admin.</response>
    /// <response code="404">Không tìm thấy ticket hoặc bình luận.</response>
    [HttpDelete("{id}/closed-override")]
    [EnableRateLimiting(ChatRateLimitingExtensions.ChatWritePolicy)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> OverrideDeleteChat(Guid ticketId, Guid id, [FromBody] ChatOverrideDeleteCommand command, CancellationToken ct)
    {
        command.TicketId = ticketId;
        command.ChatId = id;
        command.UserId = string.IsNullOrEmpty(_currentUser.UserId) ? Guid.Empty : Guid.Parse(_currentUser.UserId);
        command.UserDisplayName = _currentUser.FullName!;
        command.UserRole = ActorRoleEnum.Admin;

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Khôi phục bình luận đã bị xóa (soft-delete) — chỉ Admin.
    /// </summary>
    /// <remarks>
    /// - Set <c>IsDeleted=false, DeletedAt=null</c> và ghi activity log <c>ChatRestored</c>.
    /// - Không bị chặn khi ticket đã <c>Closed</c> (hành động data-correction của Admin).
    /// </remarks>
    /// <param name="ticketId">ID của Ticket.</param>
    /// <param name="id">ID của bình luận cần khôi phục.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Khôi phục bình luận thành công.</response>
    /// <response code="400">Bình luận chưa bị xóa.</response>
    /// <response code="403">Không có quyền khôi phục bình luận.</response>
    /// <response code="404">Không tìm thấy ticket hoặc bình luận.</response>
    [HttpPatch("{id}/restore")]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RestoreChat(Guid ticketId, Guid id, CancellationToken ct)
    {
        var command = new ChatRestoreCommand
        {
            TicketId = ticketId,
            ChatId = id,
            UserId = string.IsNullOrEmpty(_currentUser.UserId) ? Guid.Empty : Guid.Parse(_currentUser.UserId),
            UserDisplayName = _currentUser.FullName!,
            UserRole = ActorRoleEnum.Admin
        };

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }
}
