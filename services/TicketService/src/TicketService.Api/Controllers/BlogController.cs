using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.Blog;
using TicketService.Application.DTOs.Response.Blog;
using TicketService.Domain.Enums;

namespace TicketService.Api.Controllers;

/// <summary>
/// Public read-only blog endpoints (Published blogs only).
/// </summary>
[ApiController]
[Route("api/blog")]
[Authorize]
[Produces("application/json")]
public class BlogController : ControllerBase
{
    private readonly IMediator _mediator;

    public BlogController(IMediator mediator) => _mediator = mediator;

    /// <summary>Danh sách bài blog đã Published.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(CommonResponse<PaginationResponse<BlogPostListItemDTO>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] GetBlogPostListQuery query, CancellationToken ct)
    {
        query.Status = BlogPostStatusEnum.Published;
        return Ok(await _mediator.Send(query, ct));
    }

    /// <summary>Chi tiết bài blog.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(CommonResponse<BlogPostDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new GetBlogPostByIdQuery { BlogPostId = id }, ct));
}
