using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.KnowledgeBase;
using TicketService.Application.CQRS.Query.KnowledgeBase;
using TicketService.Application.DTOs.Response.KnowledgeBases;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Enums;

namespace TicketService.Api.Controllers.Admin;

/// <summary>
/// Controller quản lý KB Template dành riêng cho Admin.
/// Template là bài viết mẫu để Staff copy — không đi qua approval flow.
/// </summary>
[ApiController]
[Route("api/admin/knowledge-base/templates")]
[Authorize(Roles = "Admin")]
[Produces("application/json")]
public class AdminKbTemplateController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITicketCurrentUserService _currentUser;

    public AdminKbTemplateController(IMediator mediator, ITicketCurrentUserService currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Lấy danh sách tất cả KB Template (mọi trạng thái).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(CommonResponse<PaginationResponse<KbArticleListItemDTO>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetList([FromQuery] GetKbArticleListQuery query, CancellationToken ct)
    {
        query.IsTemplate = true;
        var result = await _mediator.Send(query, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Lấy chi tiết một KB Template theo ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(CommonResponse<KbArticleDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var query = new GetKbArticleByIdQuery { ArticleId = id };
        var result = await _mediator.Send(query, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Tạo mới KB Template.
    /// Template sẽ ở trạng thái Nháp (Draft) cho đến khi Admin xuất bản.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(CommonResponse<KbArticleActionDTO>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateKbArticleCommand command, CancellationToken ct)
    {
        command.CurrentUserId = GetCurrentUserId();
        command.CurrentUserRole = _currentUser.Role ?? string.Empty;
        command.CurrentUserName = _currentUser.FullName;
        command.IsTemplate = true;
        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Cập nhật nội dung KB Template.
    /// Nếu ở Draft: update in-place. Nếu đã Published: bump version mới.
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(CommonResponse<KbArticleDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateKbArticleCommand command, CancellationToken ct)
    {
        command.ArticleId = id;
        command.CurrentUserId = GetCurrentUserId();
        command.CurrentUserRole = _currentUser.Role ?? string.Empty;
        command.CurrentUserName = _currentUser.FullName;
        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Xuất bản KB Template từ trạng thái Nháp.
    /// </summary>
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
    /// Lưu trữ KB Template (ngừng hiển thị cho Staff).
    /// </summary>
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
    /// Hoàn tác KB Template về một phiên bản cũ.
    /// </summary>
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
    /// Xóa mềm KB Template.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status200OK)]
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
