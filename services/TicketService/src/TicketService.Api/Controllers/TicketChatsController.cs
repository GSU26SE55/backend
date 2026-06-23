using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.ChatAdd;
using TicketService.Application.CQRS.Command.ChatEdit;
using TicketService.Application.CQRS.Query.Ticket;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Enums;

namespace TicketService.Api.Controllers;

[ApiController]
[Route("api/tickets/{ticketId}/chats")]
[Authorize]
[Produces("application/json")]
public class TicketChatsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITicketCurrentUserService _currentUser;

    public TicketChatsController(IMediator mediator, ITicketCurrentUserService currentUser)
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
    public async Task<IActionResult> AddChat(Guid ticketId, [FromBody] ChatAddCommand command, CancellationToken ct)
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
    /// Sửa nội dung bình luận đã tồn tại — Author sửa được trong 15 phút kể từ lúc tạo;
    /// Manager/Admin sửa được bất cứ lúc nào nhưng phải kèm <c>EditReason</c>.
    /// </summary>
    /// <remarks>
    /// - Block khi ticket đã <c>Closed</c>.
    /// - Mỗi lần sửa lưu lại 1 bản ghi lịch sử (old/new body) và activity log <c>ChatEdited</c>.
    /// </remarks>
    /// <param name="ticketId">ID của Ticket.</param>
    /// <param name="id">ID của bình luận cần sửa.</param>
    /// <param name="command">Nội dung mới và lý do sửa (nếu không phải Author).</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Sửa bình luận thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ hoặc ticket đã đóng.</response>
    /// <response code="403">Không có quyền sửa bình luận này.</response>
    /// <response code="404">Không tìm thấy ticket hoặc bình luận.</response>
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> EditChat(Guid ticketId, Guid id, [FromBody] ChatEditCommand command, CancellationToken ct)
    {
        command.TicketId = ticketId;
        command.ChatId = id;
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
    [ProducesResponseType(typeof(CommonResponse<PaginationResponse<TicketChatDTO>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetChats(
        Guid ticketId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue)
            return Unauthorized();

        var result = await _mediator.Send(new TicketChatsQuery
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
