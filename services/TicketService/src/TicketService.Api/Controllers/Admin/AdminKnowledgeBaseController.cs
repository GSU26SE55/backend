using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.KnowledgeBase;
using TicketService.Application.DTOs.Response.KnowledgeBases;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Api.Controllers.Admin;

/// <summary>
/// Controller quản trị Knowledge Base dành cho Manager và Admin.
/// Quản lý vòng đời bài viết: Phê duyệt thay đổi, Xuất bản, Lưu trữ và Hoàn tác phiên bản.
/// </summary>
[ApiController]
[Route("api/admin/knowledge-base")]
[Authorize(Roles = "Manager,Admin")]
[Produces("application/json")]
public class AdminKnowledgeBaseController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITicketCurrentUserService _currentUser;

    public AdminKnowledgeBaseController(IMediator mediator, ITicketCurrentUserService currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Xuất bản bài viết Knowledge Base để mọi người có thể tìm thấy.
    /// </summary>
    /// <remarks>
    /// Chỉ áp dụng cho bài viết đang ở trạng thái Nháp (Draft) hoặc Chờ phê duyệt (PendingReview).
    /// </remarks>
    /// <param name="id">ID bài viết.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Xuất bản thành công.</response>
    /// <response code="409">Trạng thái hiện tại không hợp lệ để xuất bản.</response>
    [HttpPost("{id:guid}/publish")]
    [ProducesResponseType(typeof(CommonResponse<KbArticleActionDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Publish(Guid id, CancellationToken ct)
    {
        var command = new PublishKbArticleCommand
        {
            ArticleId = id,
            CurrentUserRole = _currentUser.Role ?? string.Empty
        };
        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Lưu trữ bài viết Knowledge Base (Ngừng hiển thị với Customer).
    /// </summary>
    /// <param name="id">ID bài viết.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Lưu trữ thành công.</response>
    [HttpPost("{id:guid}/archive")]
    [ProducesResponseType(typeof(CommonResponse<KbArticleActionDTO>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
    {
        var command = new ArchiveKbArticleCommand
        {
            ArticleId = id,
            CurrentUserRole = _currentUser.Role ?? string.Empty
        };
        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Chấp nhận các thay đổi của Staff và xuất bản bài viết ngay lập tức.
    /// </summary>
    /// <remarks>
    /// Chỉ áp dụng cho bài viết đang ở trạng thái Chờ phê duyệt (PendingReview).
    /// </remarks>
    /// <param name="id">ID bài viết.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Phê duyệt và xuất bản thành công.</response>
    /// <response code="409">Bài viết không ở trạng thái chờ phê duyệt.</response>
    [HttpPost("{id:guid}/approve-review")]
    [ProducesResponseType(typeof(CommonResponse<KbArticleActionDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ApproveReview(Guid id, CancellationToken ct)
    {
        var command = new ApproveReviewCommand { ArticleId = id };
        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Từ chối các thay đổi của Staff và đưa bài viết về trạng thái Nháp (Draft).
    /// </summary>
    /// <remarks>
    /// Yêu cầu Manager cung cấp lý do từ chối để Staff có thể chỉnh sửa lại.
    /// </remarks>
    /// <param name="id">ID bài viết.</param>
    /// <param name="command">Lý do từ chối.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Từ chối thành công.</response>
    [HttpPost("{id:guid}/reject-review")]
    [ProducesResponseType(typeof(CommonResponse<KbArticleActionDTO>), StatusCodes.Status200OK)]
    public async Task<IActionResult> RejectReview(Guid id, [FromBody] RejectReviewCommand command, CancellationToken ct)
    {
        command.ArticleId = id;
        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Hoàn tác nội dung bài viết về một phiên bản cũ trong lịch sử.
    /// </summary>
    /// <remarks>
    /// Sau khi hoàn tác, phiên bản cũ sẽ trở thành nội dung hiện tại và số phiên bản sẽ tăng thêm 1.
    /// </remarks>
    /// <param name="id">ID bài viết.</param>
    /// <param name="command">Số phiên bản cần khôi phục.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Hoàn tác thành công.</response>
    /// <response code="404">Không tìm thấy bài viết hoặc số phiên bản yêu cầu.</response>
    [HttpPost("{id:guid}/rollback")]
    [ProducesResponseType(typeof(CommonResponse<KbArticleActionDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Rollback(Guid id, [FromBody] RollbackKbArticleCommand command, CancellationToken ct)
    {
        command.ArticleId = id;
        command.CurrentUserId = GetCurrentUserId();
        command.CurrentUserRole = _currentUser.Role ?? string.Empty;
        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Xóa mềm bài viết Knowledge Base (Chỉ dành cho Admin).
    /// </summary>
    /// <remarks>
    /// Đánh dấu bài viết là đã xóa nhưng không xóa dữ liệu vật lý trong Database.
    /// </remarks>
    /// <param name="id">ID bài viết.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Xóa thành công.</response>
    /// <response code="403">Chỉ có Admin mới có quyền thực hiện thao tác này.</response>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var command = new DeleteKbArticleCommand { ArticleId = id };
        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    private Guid GetCurrentUserId()
    {
        return string.IsNullOrEmpty(_currentUser.UserId) ? Guid.Empty : Guid.Parse(_currentUser.UserId);
    }
}
