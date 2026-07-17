using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.Blog;
using TicketService.Application.DTOs.Response.Blog;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Api.Controllers.Admin;

/// <summary>
/// Admin/Manager blog management: AI generate, publish, archive, delete.
/// </summary>
[ApiController]
[Route("api/admin/blog")]
[Authorize(Roles = "Manager,Admin")]
[Produces("application/json")]
public class AdminBlogController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITicketCurrentUserService _currentUser;

    public AdminBlogController(IMediator mediator, ITicketCurrentUserService currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    /// <summary>Tạo blog từ KB article bằng AI (async — trả 202 ngay).</summary>
    [HttpPost("generate-from-kb/{kbId:guid}")]
    [ProducesResponseType(typeof(CommonResponse<BlogPostActionDTO>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GenerateFromKb(Guid kbId, CancellationToken ct)
    {
        var command = new GenerateBlogFromKbCommand
        {
            KbArticleId = kbId,
            CurrentUserId = GetCurrentUserId(),
        };
        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Publish bài blog (Draft → Published).</summary>
    [HttpPost("{id:guid}/publish")]
    [ProducesResponseType(typeof(CommonResponse<BlogPostActionDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Publish(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new PublishBlogPostCommand { BlogPostId = id }, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Archive bài blog.</summary>
    [HttpPost("{id:guid}/archive")]
    [ProducesResponseType(typeof(CommonResponse<BlogPostActionDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new ArchiveBlogPostCommand { BlogPostId = id }, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Xóa (soft delete) bài blog.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(CommonResponse<BlogPostActionDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new DeleteBlogPostCommand { BlogPostId = id }, ct);
        return StatusCode(result.StatusCode, result);
    }

    private Guid GetCurrentUserId() =>
        string.IsNullOrEmpty(_currentUser.UserId) ? Guid.Empty : Guid.Parse(_currentUser.UserId);
}
