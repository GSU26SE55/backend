using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.Blog;
using TicketService.Application.CQRS.Query.Blog;
using TicketService.Application.DTOs.Response.Blog;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Api.Controllers;

/// <summary>
/// Blog endpoints dành cho nội bộ (Staff, Manager, Admin) — soạn thảo, cập nhật, xem version.
/// </summary>
[ApiController]
[Route("api/internal/blog")]
[Authorize(Roles = "Staff,Manager,Admin")]
[Produces("application/json")]
public class InternalBlogController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITicketCurrentUserService _currentUser;

    public InternalBlogController(IMediator mediator, ITicketCurrentUserService currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    /// <summary>Danh sách bài blog (Staff/Manager/Admin — có thể filter theo status).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(CommonResponse<PaginationResponse<BlogPostListItemDTO>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] GetBlogPostListQuery query, CancellationToken ct)
    {
        query.IsInternal = true;
        return Ok(await _mediator.Send(query, ct));
    }

    /// <summary>Tạo bài blog thủ công (Draft).</summary>
    [HttpPost]
    [ProducesResponseType(typeof(CommonResponse<BlogPostActionDTO>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateBlogPostCommand command, CancellationToken ct)
    {
        command.CurrentUserId = GetCurrentUserId();
        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Cập nhật bài blog (có optimistic concurrency check).</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(CommonResponse<BlogPostActionDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateBlogPostCommand command, CancellationToken ct)
    {
        command.BlogPostId = id;
        command.CurrentUserId = GetCurrentUserId();
        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Danh sách versions của bài blog.</summary>
    [HttpGet("{id:guid}/versions")]
    [ProducesResponseType(typeof(CommonResponse<List<BlogPostVersionDTO>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetVersions(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new GetBlogPostVersionsQuery { BlogPostId = id }, ct));

    /// <summary>So sánh 2 versions của bài blog.</summary>
    [HttpGet("{id:guid}/compare")]
    [ProducesResponseType(typeof(CommonResponse<BlogDiffDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Compare(Guid id, [FromQuery] CompareBlogPostVersionsQuery query, CancellationToken ct)
    {
        query.BlogPostId = id;
        var result = await _mediator.Send(query, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Danh sách blog templates (Staff/Manager/Admin).</summary>
    [HttpGet("templates")]
    [ProducesResponseType(typeof(CommonResponse<List<BlogTemplateDTO>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllTemplates([FromQuery] GetBlogTemplateListQuery query, CancellationToken ct)
        => Ok(await _mediator.Send(query, ct));

    /// <summary>Chi tiết blog template theo ID (Staff/Manager/Admin).</summary>
    [HttpGet("templates/{id:guid}")]
    [ProducesResponseType(typeof(CommonResponse<BlogTemplateDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTemplateById(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new GetBlogTemplateByIdQuery { TemplateId = id }, ct));

    private Guid GetCurrentUserId() =>
        string.IsNullOrEmpty(_currentUser.UserId) ? Guid.Empty : Guid.Parse(_currentUser.UserId);
}
