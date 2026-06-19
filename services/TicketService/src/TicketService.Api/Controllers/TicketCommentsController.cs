using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.CommentAdd;
using TicketService.Application.CQRS.Query.Ticket;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Enums;

namespace TicketService.Api.Controllers;

[ApiController]
[Route("api/tickets/{ticketId}/comments")]
[Authorize]
[Produces("application/json")]
public class TicketCommentsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITicketCurrentUserService _currentUser;

    public TicketCommentsController(IMediator mediator, ITicketCurrentUserService currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Thêm bình luận vào Ticket (Customer/Staff/Manager) — Internal=true chỉ Staff/Manager thấy (hidden từ Customer). Hỗ trợ attachment file.
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
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddComment(Guid ticketId, [FromBody] CommentAddCommand command, CancellationToken ct)
    {
        command.TicketId = ticketId;
        command.UserId = string.IsNullOrEmpty(_currentUser.UserId) ? Guid.Empty : Guid.Parse(_currentUser.UserId);
        command.UserDisplayName = _currentUser.FullName ?? "Unknown";

        var roleStr = _currentUser.Role;
        var userRole = ActorRoleEnum.Staff; // Default
        if (roleStr == "Customer")
            userRole = ActorRoleEnum.Customer;
        else if (roleStr == "Manager")
            userRole = ActorRoleEnum.Manager;
        else if (roleStr == "Admin")
            userRole = ActorRoleEnum.Admin;

        command.UserRole = userRole;

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Lấy danh sách bình luận của Ticket (phân trang) — sort theo CreatedAt ASC (timeline conversation); Customer KHÔNG thấy comment có Internal=true.
    /// </summary>
    /// <remarks>
    /// - Nếu là Customer: Chỉ xem được các bình luận công khai (IsInternal = false).
    /// - Nếu là Staff/Manager/Admin: Xem được tất cả bình luận bao gồm cả nội bộ.
    /// </remarks>
    /// <param name="ticketId">ID của Ticket.</param>
    /// <param name="page">Số trang (mặc định 1).</param>
    /// <param name="pageSize">Kích thước trang (mặc định 10).</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Lấy danh sách thành công.</response>
    /// <response code="403">Không có quyền truy cập ticket.</response>
    /// <response code="404">Không tìm thấy ticket.</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<PaginationResponse<TicketCommentDTO>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetComments(
        Guid ticketId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue)
            return Unauthorized();

        var result = await _mediator.Send(new TicketCommentsQuery
        {
            TicketId = ticketId,
            ActorUserId = actorId.Value,
            ActorRoles = GetCurrentRoles(),
            PageNumber = page,
            PageSize = pageSize
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
}
