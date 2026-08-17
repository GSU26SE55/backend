using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using SharedContracts.Common.Responses;
using TicketService.Api.Extensions;
using TicketService.Application.CQRS.Command.ChatAi;
using TicketService.Application.CQRS.Command.Chats;
using TicketService.Application.CQRS.Query.ChatKbSuggestions;
using TicketService.Application.CQRS.Query.Chats;
using TicketService.Application.CQRS.Query.Ticket;
using TicketService.Application.DTOs.Response.Chats;
using TicketService.Application.DTOs.Response.KnowledgeBases;
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
    private readonly ILogger<TicketChatsController> _logger;

    public TicketChatsController(IMediator mediator, ITicketCurrentUserService currentUser, ILogger<TicketChatsController> logger)
    {
        _mediator = mediator;
        _logger = logger;
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
    [EnableRateLimiting(ChatRateLimitingExtensions.ChatWritePolicy)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddChat(Guid ticketId, [FromBody] ChatAddCommand command, CancellationToken ct)
    {
        command.TicketId = ticketId;
        command.UserId = string.IsNullOrEmpty(_currentUser.UserId) ? Guid.Empty : Guid.Parse(_currentUser.UserId);
        command.UserDisplayName = _currentUser.FullName!;

        var roleStr = _currentUser.Role;
        var userRole = ActorRoleEnum.Staff; // Default
        if (roleStr == "Customer")
            userRole = ActorRoleEnum.Customer;
        else if (roleStr == "Manager")
            userRole = ActorRoleEnum.Manager;
        else if (roleStr == "Admin")
            userRole = ActorRoleEnum.Admin;

        command.UserRole = userRole;
        command.UserPermissions = _currentUser.Permissions.ToList();

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Sửa nội dung bình luận đã tồn tại — chỉ Author sửa được trong 15 phút kể từ lúc tạo.
    /// </summary>
    /// <remarks>
    /// - Block khi ticket đã <c>Closed</c>.
    /// - Mỗi lần sửa lưu lại 1 bản ghi lịch sử (old/new body) và activity log <c>ChatEdited</c>.
    /// </remarks>
    /// <param name="ticketId">ID của Ticket.</param>
    /// <param name="id">ID của bình luận cần sửa.</param>
    /// <param name="command">Nội dung mới.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Sửa bình luận thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ hoặc ticket đã đóng.</response>
    /// <response code="403">Không có quyền sửa bình luận này.</response>
    /// <response code="404">Không tìm thấy ticket hoặc bình luận.</response>
    [HttpPut("{id}")]
    [EnableRateLimiting(ChatRateLimitingExtensions.ChatWritePolicy)]
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
        command.UserDisplayName = _currentUser.FullName!;

        var roleStr = _currentUser.Role;
        var userRole = ActorRoleEnum.Staff; // Default
        if (roleStr == "Customer")
            userRole = ActorRoleEnum.Customer;
        else if (roleStr == "Manager")
            userRole = ActorRoleEnum.Manager;
        else if (roleStr == "Admin")
            userRole = ActorRoleEnum.Admin;

        command.UserRole = userRole;
        command.UserPermissions = _currentUser.Permissions.ToList();

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Xóa (soft-delete) bình luận — chỉ Author xóa được của mình.
    /// </summary>
    /// <remarks>
    /// - Block khi ticket đã <c>Closed</c>.
    /// - Set <c>IsDeleted=true, DeletedAt</c> và ghi activity log <c>ChatDeleted</c>.
    /// </remarks>
    /// <param name="ticketId">ID của Ticket.</param>
    /// <param name="id">ID của bình luận cần xóa.</param>
    /// <param name="command">Request body (không cần thiết, có thể bỏ qua).</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Xóa bình luận thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ hoặc ticket đã đóng.</response>
    /// <response code="403">Không có quyền xóa bình luận này.</response>
    /// <response code="404">Không tìm thấy ticket hoặc bình luận.</response>
    [HttpDelete("{id}")]
    [EnableRateLimiting(ChatRateLimitingExtensions.ChatWritePolicy)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteChat(Guid ticketId, Guid id, [FromBody] ChatDeleteCommand? command, CancellationToken ct)
    {
        command ??= new ChatDeleteCommand();
        command.TicketId = ticketId;
        command.ChatId = id;
        command.UserId = string.IsNullOrEmpty(_currentUser.UserId) ? Guid.Empty : Guid.Parse(_currentUser.UserId);
        command.UserDisplayName = _currentUser.FullName!;

        var roleStr = _currentUser.Role;
        var userRole = ActorRoleEnum.Staff; // Default
        if (roleStr == "Customer")
            userRole = ActorRoleEnum.Customer;
        else if (roleStr == "Manager")
            userRole = ActorRoleEnum.Manager;
        else if (roleStr == "Admin")
            userRole = ActorRoleEnum.Admin;

        command.UserRole = userRole;
        command.UserPermissions = _currentUser.Permissions.ToList();

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
    /// <param name="query">Filter params: page, pageSize, search, authorRole, isInternal, ...</param>
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
        [FromQuery] TicketChatsQuery query,
        CancellationToken ct = default)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue)
            return Unauthorized();

        query.TicketId = ticketId;
        query.ActorUserId = actorId.Value;
        query.ActorRoles = GetCurrentRoles();

        var result = await _mediator.Send(query, ct);

        // Auto mark-read trang hiện tại (#541) — Command riêng, không ảnh hưởng response của Query.
        var chatIds = result.Data?.Items?
            .Where(c => Guid.TryParse(c.Id, out _))
            .Select(c => Guid.Parse(c.Id))
            .ToList();

        if (chatIds != null && chatIds.Count > 0)
        {
            try
            {
                await _mediator.Send(new ChatMarkAsReadCommand
                {
                    TicketId = ticketId,
                    UserId = actorId.Value,
                    UserRole = ResolveActorRole(_currentUser.Role),
                    ActorRoles = GetCurrentRoles(),
                    ChatIds = chatIds
                }, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[GetChats] Auto mark-read failed for ticket {TicketId}, user {UserId}", ticketId, actorId.Value);
            }
        }

        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Lấy chi tiết đầy đủ 1 bình luận (edit_count, attachment list, mention/reaction — rỗng tạm thời).
    /// </summary>
    /// <param name="ticketId">ID của Ticket.</param>
    /// <param name="id">ID của bình luận.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Lấy chi tiết thành công.</response>
    /// <response code="403">Không có quyền truy cập ticket.</response>
    /// <response code="404">Không tìm thấy ticket hoặc bình luận.</response>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(CommonResponse<TicketChatDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetChatById(Guid ticketId, Guid id, CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue)
            return Unauthorized();

        var result = await _mediator.Send(new ChatGetByIdQuery
        {
            TicketId = ticketId,
            ChatId = id,
            ActorUserId = actorId.Value,
            ActorRoles = GetCurrentRoles()
        }, ct);

        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Lấy lịch sử các bản sửa (old/new body) của 1 bình luận — Customer chỉ xem được history của chat do chính mình viết.
    /// </summary>
    /// <param name="ticketId">ID của Ticket.</param>
    /// <param name="id">ID của bình luận.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Lấy lịch sử thành công.</response>
    /// <response code="403">Không có quyền xem lịch sử bình luận này.</response>
    /// <response code="404">Không tìm thấy ticket hoặc bình luận.</response>
    [HttpGet("{id}/history")]
    [ProducesResponseType(typeof(CommonResponse<List<ChatEditHistoryDTO>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetChatHistory(Guid ticketId, Guid id, CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue)
            return Unauthorized();

        var result = await _mediator.Send(new ChatHistoryQuery
        {
            TicketId = ticketId,
            ChatId = id,
            ActorUserId = actorId.Value,
            ActorRoles = GetCurrentRoles()
        }, ct);

        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Thêm đính kèm vào bình luận đã tồn tại — Author hoặc Manager/Admin. Tối đa 10 file/bình luận, 50MB/file, MIME whitelist.
    /// </summary>
    /// <param name="ticketId">ID của Ticket.</param>
    /// <param name="id">ID của bình luận.</param>
    /// <param name="command">Thông tin file đã upload (FileId tham chiếu file lưu ở nơi khác).</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="201">Thêm đính kèm thành công.</response>
    /// <response code="400">Vượt giới hạn số lượng/kích thước/loại file, hoặc ticket đã đóng.</response>
    /// <response code="403">Không có quyền thêm đính kèm.</response>
    /// <response code="404">Không tìm thấy ticket hoặc bình luận.</response>
    [HttpPost("{id}/attachments")]
    [ProducesResponseType(typeof(CommonResponse<TicketAttachmentDTO>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddChatAttachment(Guid ticketId, Guid id, [FromBody] ChatAttachmentAddCommand command, CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue)
            return Unauthorized();

        command.TicketId = ticketId;
        command.ChatId = id;
        command.UserId = actorId.Value;
        command.UserRole = ResolveActorRole(_currentUser.Role);

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Thêm nhiều đính kèm cùng lúc — kiểm tra slot trống trước, trả lỗi ngay nếu vượt giới hạn 10 file/bình luận.
    /// </summary>
    /// <param name="ticketId">ID của Ticket.</param>
    /// <param name="id">ID của bình luận.</param>
    /// <param name="command">Danh sách file cần thêm.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="201">Thêm đính kèm thành công, trả về danh sách file đã lưu.</response>
    /// <response code="400">Vượt giới hạn số lượng/kích thước/loại file, hoặc ticket đã đóng.</response>
    /// <response code="403">Không có quyền thêm đính kèm.</response>
    /// <response code="404">Không tìm thấy ticket hoặc bình luận.</response>
    [HttpPost("{id}/attachments/batch")]
    [ProducesResponseType(typeof(CommonResponse<List<TicketAttachmentDTO>>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddChatAttachmentBatch(Guid ticketId, Guid id, [FromBody] ChatAttachmentBatchAddCommand command, CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue)
            return Unauthorized();

        command.TicketId = ticketId;
        command.ChatId = id;
        command.UserId = actorId.Value;
        command.UserRole = ResolveActorRole(_currentUser.Role);

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Xóa (soft-delete) đính kèm — Author của bình luận hoặc Manager/Admin.
    /// </summary>
    /// <param name="ticketId">ID của Ticket.</param>
    /// <param name="id">ID của bình luận.</param>
    /// <param name="attachmentId">ID của đính kèm cần xóa.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Xóa đính kèm thành công.</response>
    /// <response code="403">Không có quyền xóa đính kèm này.</response>
    /// <response code="404">Không tìm thấy ticket, bình luận hoặc đính kèm.</response>
    [HttpDelete("{id}/attachments/{attachmentId}")]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveChatAttachment(Guid ticketId, Guid id, Guid attachmentId, CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue)
            return Unauthorized();

        var command = new ChatAttachmentRemoveCommand
        {
            TicketId = ticketId,
            ChatId = id,
            AttachmentId = attachmentId,
            UserId = actorId.Value,
            UserRole = ResolveActorRole(_currentUser.Role)
        };

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Lấy danh sách đính kèm của 1 bình luận.
    /// </summary>
    /// <param name="ticketId">ID của Ticket.</param>
    /// <param name="id">ID của bình luận.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Lấy danh sách thành công.</response>
    /// <response code="403">Không có quyền truy cập ticket.</response>
    /// <response code="404">Không tìm thấy ticket hoặc bình luận.</response>
    [HttpGet("{id}/attachments")]
    [ProducesResponseType(typeof(CommonResponse<List<TicketAttachmentDTO>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListChatAttachments(Guid ticketId, Guid id, CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue)
            return Unauthorized();

        var result = await _mediator.Send(new ChatAttachmentListQuery
        {
            TicketId = ticketId,
            ChatId = id,
            ActorUserId = actorId.Value,
            ActorRoles = GetCurrentRoles()
        }, ct);

        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Tổng hợp toàn bộ files đã gửi qua chat trong ticket — Customer chỉ thấy files từ chat IsInternal=false.
    /// </summary>
    /// <param name="ticketId">ID của Ticket.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Lấy danh sách thành công.</response>
    /// <response code="403">Không có quyền truy cập ticket.</response>
    /// <response code="404">Không tìm thấy ticket.</response>
    [HttpGet("files")]
    [ProducesResponseType(typeof(CommonResponse<List<TicketAttachmentDTO>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetChatFiles(Guid ticketId, CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue)
            return Unauthorized();

        var result = await _mediator.Send(new ChatFileSummaryQuery
        {
            TicketId = ticketId,
            ActorUserId = actorId.Value,
            ActorRoles = GetCurrentRoles()
        }, ct);

        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Lấy URL download của đính kèm sau khi kiểm tra trạng thái virus scan.
    /// Trả 451 nếu file bị nhiễm virus, 202 nếu đang scan, 200+URL nếu sạch.
    /// </summary>
    /// <param name="ticketId">ID của Ticket.</param>
    /// <param name="id">ID của bình luận.</param>
    /// <param name="attachmentId">ID của đính kèm.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Trả về download URL.</response>
    /// <response code="202">File đang được scan, thử lại sau.</response>
    /// <response code="403">Không có quyền truy cập.</response>
    /// <response code="404">Không tìm thấy ticket, bình luận hoặc đính kèm.</response>
    /// <response code="451">File bị nhiễm virus — không thể tải xuống.</response>
    [HttpGet("{id}/attachments/{attachmentId}/download")]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(CommonResponse<string>), 451)]
    public async Task<IActionResult> DownloadChatAttachment(Guid ticketId, Guid id, Guid attachmentId, CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue)
            return Unauthorized();

        var result = await _mediator.Send(new ChatAttachmentDownloadQuery
        {
            TicketId = ticketId,
            ChatId = id,
            AttachmentId = attachmentId,
            ActorUserId = actorId.Value,
            ActorRoles = GetCurrentRoles()
        }, ct);

        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Trả lời 1 bình luận — tối đa 1 cấp (không thể reply vào 1 reply).
    /// </summary>
    /// <param name="ticketId">ID của Ticket.</param>
    /// <param name="id">ID của bình luận cha (parent).</param>
    /// <param name="command">Nội dung trả lời.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="201">Trả lời bình luận thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ hoặc parent đã là 1 reply.</response>
    /// <response code="404">Không tìm thấy ticket hoặc bình luận cha.</response>
    [HttpPost("{id}/replies")]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddChatReply(Guid ticketId, Guid id, [FromBody] ChatReplyCommand command, CancellationToken ct)
    {
        command.TicketId = ticketId;
        command.ParentChatId = id;
        command.UserId = string.IsNullOrEmpty(_currentUser.UserId) ? Guid.Empty : Guid.Parse(_currentUser.UserId);
        command.UserDisplayName = _currentUser.FullName!;
        command.UserRole = ResolveActorRole(_currentUser.Role);

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Lấy danh sách reply của 1 bình luận (phân trang) — sort theo CreatedAt ASC.
    /// </summary>
    /// <param name="ticketId">ID của Ticket.</param>
    /// <param name="id">ID của bình luận cha (parent).</param>
    /// <param name="page">Số trang (mặc định 1).</param>
    /// <param name="pageSize">Kích thước trang (mặc định 10).</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Lấy danh sách thành công.</response>
    /// <response code="403">Không có quyền truy cập ticket.</response>
    /// <response code="404">Không tìm thấy ticket hoặc bình luận cha.</response>
    [HttpGet("{id}/replies")]
    [ProducesResponseType(typeof(CommonResponse<PaginationResponse<TicketChatDTO>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetChatReplies(
        Guid ticketId,
        Guid id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue)
            return Unauthorized();

        var result = await _mediator.Send(new ChatRepliesQuery
        {
            TicketId = ticketId,
            ParentChatId = id,
            ActorUserId = actorId.Value,
            ActorRoles = GetCurrentRoles(),
            PageNumber = page,
            PageSize = pageSize
        }, ct);

        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Pin 1 bình luận — chỉ Staff/Manager/Admin. Tối đa 3 bình luận pin/ticket.
    /// </summary>
    /// <param name="ticketId">ID của Ticket.</param>
    /// <param name="id">ID của bình luận cần pin.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Pin bình luận thành công.</response>
    /// <response code="400">Bình luận đã được pin hoặc đã đạt giới hạn 3 pin/ticket.</response>
    /// <response code="403">Không có quyền pin bình luận.</response>
    /// <response code="404">Không tìm thấy ticket hoặc bình luận.</response>
    [HttpPost("{id}/pin")]
    [Authorize(Roles = "Staff,Manager,Admin")]
    [EnableRateLimiting(ChatRateLimitingExtensions.ChatWritePolicy)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PinChat(Guid ticketId, Guid id, CancellationToken ct)
    {
        var command = new ChatPinCommand
        {
            TicketId = ticketId,
            ChatId = id,
            UserId = string.IsNullOrEmpty(_currentUser.UserId) ? Guid.Empty : Guid.Parse(_currentUser.UserId),
            UserDisplayName = _currentUser.FullName!,
            UserRole = ResolveActorRole(_currentUser.Role),
            UserPermissions = _currentUser.Permissions.ToList()
        };

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Unpin 1 bình luận — chỉ Staff/Manager/Admin.
    /// </summary>
    /// <param name="ticketId">ID của Ticket.</param>
    /// <param name="id">ID của bình luận cần unpin.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Unpin bình luận thành công.</response>
    /// <response code="400">Bình luận chưa được pin.</response>
    /// <response code="403">Không có quyền unpin bình luận.</response>
    /// <response code="404">Không tìm thấy ticket hoặc bình luận.</response>
    [HttpDelete("{id}/pin")]
    [Authorize(Roles = "Staff,Manager,Admin")]
    [EnableRateLimiting(ChatRateLimitingExtensions.ChatWritePolicy)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UnpinChat(Guid ticketId, Guid id, CancellationToken ct)
    {
        var command = new ChatUnpinCommand
        {
            TicketId = ticketId,
            ChatId = id,
            UserId = string.IsNullOrEmpty(_currentUser.UserId) ? Guid.Empty : Guid.Parse(_currentUser.UserId),
            UserDisplayName = _currentUser.FullName!,
            UserRole = ResolveActorRole(_currentUser.Role),
            UserPermissions = _currentUser.Permissions.ToList()
        };

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Thêm reaction vào 1 bình luận — idempotent nếu đã reaction cùng loại.
    /// </summary>
    /// <param name="ticketId">ID của Ticket.</param>
    /// <param name="id">ID của bình luận.</param>
    /// <param name="command">Loại reaction.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Thêm reaction thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ.</response>
    /// <response code="404">Không tìm thấy ticket hoặc bình luận.</response>
    [HttpPost("{id}/reactions")]
    [ProducesResponseType(typeof(ChatReactionActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddChatReaction(Guid ticketId, Guid id, [FromBody] ChatReactionAddCommand command, CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue)
            return Unauthorized();

        command.TicketId = ticketId;
        command.ChatId = id;
        command.UserId = actorId.Value;
        command.UserRole = ResolveActorRole(_currentUser.Role);
        command.ActorRoles = GetCurrentRoles();

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Xóa reaction khỏi 1 bình luận — no-op nếu chưa reaction loại này.
    /// </summary>
    /// <param name="ticketId">ID của Ticket.</param>
    /// <param name="id">ID của bình luận.</param>
    /// <param name="type">Loại reaction cần xóa.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Xóa reaction thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ.</response>
    /// <response code="404">Không tìm thấy ticket hoặc bình luận.</response>
    [HttpDelete("{id}/reactions")]
    [ProducesResponseType(typeof(ChatReactionActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveChatReaction(Guid ticketId, Guid id, [FromQuery] ReactionTypeEnum type, CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue)
            return Unauthorized();

        var command = new ChatReactionRemoveCommand
        {
            TicketId = ticketId,
            ChatId = id,
            UserId = actorId.Value,
            UserRole = ResolveActorRole(_currentUser.Role),
            ActorRoles = GetCurrentRoles(),
            ReactionType = type
        };

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Lấy reaction aggregate của 1 bình luận — group theo 5 loại reaction.
    /// </summary>
    /// <param name="ticketId">ID của Ticket.</param>
    /// <param name="id">ID của bình luận.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Lấy reaction thành công.</response>
    /// <response code="404">Không tìm thấy ticket hoặc bình luận.</response>
    [HttpGet("{id}/reactions")]
    [ProducesResponseType(typeof(CommonResponse<TicketService.Application.DTOs.Response.Chats.TicketChatReactionsAggregateDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetChatReactions(Guid ticketId, Guid id, CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue)
            return Unauthorized();

        var result = await _mediator.Send(new ChatReactionsQuery
        {
            TicketId = ticketId,
            ChatId = id,
            ActorUserId = actorId.Value,
            ActorRoles = GetCurrentRoles()
        }, ct);

        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Mark-read 1 hoặc nhiều chat (bulk) — đồng thời dùng cho auto mark-read khi GetList.
    /// </summary>
    /// <param name="ticketId">ID của Ticket.</param>
    /// <param name="command">Danh sách ChatId cần mark-read.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Mark-read thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ.</response>
    /// <response code="503">Hàng đợi read-receipt đầy — một phần chat chưa được đánh dấu, client nên thử lại.</response>
    [HttpPost("mark-read")]
    [ProducesResponseType(typeof(ChatMarkAsReadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ChatMarkAsReadResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> MarkChatsAsRead(Guid ticketId, [FromBody] ChatMarkAsReadCommand command, CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue)
            return Unauthorized();

        command.TicketId = ticketId;
        command.UserId = actorId.Value;
        command.UserRole = ResolveActorRole(_currentUser.Role);
        command.ActorRoles = GetCurrentRoles();

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Lấy danh sách user đã đọc 1 bình luận — chỉ Staff/Manager/Admin.
    /// </summary>
    /// <param name="ticketId">ID của Ticket.</param>
    /// <param name="id">ID của bình luận.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Lấy danh sách thành công.</response>
    /// <response code="404">Không tìm thấy ticket hoặc bình luận.</response>
    [HttpGet("{id}/readers")]
    [Authorize(Roles = "Staff,Manager,Admin")]
    [ProducesResponseType(typeof(ChatReadersResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetChatReaders(Guid ticketId, Guid id, CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue)
            return Unauthorized();

        var result = await _mediator.Send(new ChatReadersQuery
        {
            TicketId = ticketId,
            ChatId = id,
            ActorUserId = actorId.Value,
            ActorRoles = GetCurrentRoles()
        }, ct);

        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Số chat chưa đọc của user hiện tại trên ticket này.
    /// </summary>
    /// <param name="ticketId">ID của Ticket.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Lấy số chưa đọc thành công.</response>
    /// <response code="403">Không có quyền truy cập ticket.</response>
    /// <response code="404">Không tìm thấy ticket.</response>
    [HttpGet("unread-count")]
    [ProducesResponseType(typeof(TicketUnreadCountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTicketUnreadCount(Guid ticketId, CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue)
            return Unauthorized();

        var result = await _mediator.Send(new TicketUnreadCountQuery
        {
            TicketId = ticketId,
            ActorUserId = actorId.Value,
            ActorRoles = GetCurrentRoles()
        }, ct);

        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("cursor")]
    [ProducesResponseType(typeof(CommonResponse<CursorPaginationResponse<TicketChatDTO>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetChatsCursor(
        Guid ticketId,
        [FromQuery] string? cursor = null,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue)
            return Unauthorized();

        var result = await _mediator.Send(new TicketChatsCursorQuery
        {
            TicketId = ticketId,
            ActorUserId = actorId.Value,
            ActorRoles = GetCurrentRoles(),
            Cursor = cursor,
            Limit = limit
        }, ct);

        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Gắn bài viết KB vào 1 chat — Staff/Manager/Admin. Tham chiếu được lưu kèm ChatId.
    /// </summary>
    [HttpPost("{id}/attach-kb")]
    [Authorize(Roles = "Staff,Manager,Admin")]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AttachKbToChat(Guid ticketId, Guid id, [FromBody] ChatAttachKbReferenceCommand command, CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue)
            return Unauthorized();

        command.TicketId = ticketId;
        command.ChatId = id;
        command.CurrentUserId = actorId.Value;

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Chuyển nội dung chat thành KB Draft — Staff/Manager/Admin. Tạo bài viết KB với Status=Draft.
    /// </summary>
    [HttpPost("{id}/to-kb-draft")]
    [Authorize(Roles = "Staff,Manager,Admin")]
    [ProducesResponseType(typeof(CommonResponse<KbArticleActionDTO>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ConvertChatToKbDraft(Guid ticketId, Guid id, [FromBody] ChatConvertToKbDraftCommand command, CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue)
            return Unauthorized();

        command.TicketId = ticketId;
        command.ChatId = id;
        command.CurrentUserId = actorId.Value;

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Gợi ý KB articles dựa trên nội dung chat (full-text match theo category + keywords).
    /// </summary>
    [HttpGet("{id}/kb-suggestions")]
    [Authorize(Roles = "Staff,Manager,Admin")]
    [ProducesResponseType(typeof(CommonResponse<List<KbArticleSuggestDTO>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetChatKbSuggestions(
        Guid ticketId,
        Guid id,
        [FromQuery] int topN = 3,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ChatKbSuggestionsQuery
        {
            TicketId = ticketId,
            ChatId = id,
            TopN = topN
        }, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>AI-generated chat suggestions với PII mask (#559). Trả 3 gợi ý theo intent được chọn.</summary>
    [HttpPost("suggest")]
    [Authorize(Roles = "Staff,Manager,Admin")]
    [ProducesResponseType(typeof(ChatSuggestResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> SuggestChat(
        Guid ticketId,
        [FromBody] ChatSuggestCommand command,
        CancellationToken ct = default)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue)
            return Unauthorized();

        command.TicketId = ticketId;
        command.CurrentUserId = actorId.Value;

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("summarize")]
    [Authorize(Roles = "Staff,Manager,Admin")]
    [ProducesResponseType(typeof(ChatSummarizeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Summarize(Guid ticketId, CancellationToken ct = default)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue)
            return Unauthorized();

        var result = await _mediator.Send(new ChatSummarizeCommand
        {
            TicketId = ticketId,
            CurrentUserId = actorId.Value
        }, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Dịch nội dung chat sang ngôn ngữ target (#562). Cache 2 lớp: Redis (30 ngày) → DB → Gemini AI.
    /// </summary>
    /// <param name="ticketId">ID của Ticket.</param>
    /// <param name="id">ID của Chat cần dịch.</param>
    /// <param name="to">Mã ngôn ngữ đích theo ISO 639-1 (ví dụ: "en", "vi", "fr").</param>
    /// <param name="ct">Token hủy request.</param>
    [HttpPost("{id}/translate")]
    [ProducesResponseType(typeof(ChatTranslateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TranslateChat(Guid ticketId, Guid id, [FromQuery] string? to, CancellationToken ct = default)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue)
            return Unauthorized();

        var result = await _mediator.Send(new ChatTranslateCommand
        {
            TicketId = ticketId,
            ChatId = id,
            CurrentUserId = actorId.Value,
            TargetLanguage = to ?? string.Empty,
            CurrentUserRoles = GetCurrentRoles()
        }, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Xóa nhiều chat của chính author trong 1 request — partial success (skip chat không thuộc author).
    /// Tối đa 50 ChatIds/request.
    /// </summary>
    /// <param name="ticketId">ID của Ticket.</param>
    /// <param name="command">Danh sách ChatId cần xóa.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Trả về số lượng đã xóa và danh sách bị skip.</response>
    /// <response code="400">Dữ liệu không hợp lệ hoặc ticket đã đóng.</response>
    /// <response code="404">Không tìm thấy ticket.</response>
    [HttpDelete("bulk")]
    [EnableRateLimiting(ChatRateLimitingExtensions.ChatWritePolicy)]
    [ProducesResponseType(typeof(ChatBulkDeleteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> BulkDeleteChats(Guid ticketId, [FromBody] ChatBulkDeleteCommand command, CancellationToken ct)
    {
        command.TicketId = ticketId;
        command.UserId = string.IsNullOrEmpty(_currentUser.UserId) ? Guid.Empty : Guid.Parse(_currentUser.UserId);
        command.UserDisplayName = _currentUser.FullName!;
        command.UserRole = ResolveActorRole(_currentUser.Role);
        command.UserPermissions = _currentUser.Permissions.ToList();

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Manager ACK escalation review — transitions saga Pending → Reviewed. #566.</summary>
    [HttpPost("{id}/escalation-review/ack")]
    [Authorize(Roles = "Manager,Admin")]
    public async Task<IActionResult> AckEscalationReview(Guid ticketId, Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
            return Unauthorized();

        var roleStr = User.FindFirst(ClaimTypes.Role)?.Value;
        var result = await _mediator.Send(new ChatEscalationReviewAckCommand
        {
            TicketId = ticketId,
            ChatId = id,
            CurrentUserId = userId.Value,
            CurrentUserRole = ResolveActorRole(roleStr)
        }, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Tạo chat audio placeholder từ metadata file đã upload và xếp hàng transcribe bất đồng bộ.
    /// </summary>
    [HttpPost("voice")]
    [EnableRateLimiting(ChatRateLimitingExtensions.ChatWritePolicy)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> VoiceTranscribe(
        Guid ticketId,
        [FromBody] ChatVoiceTranscribeCommand command,
        CancellationToken ct)
    {
        command.TicketId = ticketId;
        command.UserId = _currentUser.UserId is { Length: > 0 } uid && Guid.TryParse(uid, out var parsed) ? parsed : Guid.Empty;
        command.UserDisplayName = _currentUser.FullName!;
        command.UserRole = ResolveActorRole(User.FindFirst(ClaimTypes.Role)?.Value);
        command.UserPermissions = _currentUser.Permissions.ToList();

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("{id}/voice/retry")]
    [EnableRateLimiting(ChatRateLimitingExtensions.ChatWritePolicy)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> RetryVoiceTranscription(Guid ticketId, Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue)
            return Unauthorized();
        var result = await _mediator.Send(new ChatVoiceTranscriptionRetryCommand
        {
            TicketId = ticketId,
            ChatId = id,
            UserId = userId.Value,
            UserRole = ResolveActorRole(_currentUser.Role)
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
