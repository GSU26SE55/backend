using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.Blog;
using TicketService.Application.CQRS.Query.Blog;
using TicketService.Application.DTOs.Response.Blog;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Api.Controllers.Admin;

/// <summary>
/// Blog template management (Admin-managed, Staff/Manager read).
/// </summary>
[ApiController]
[Route("api/admin/blog/templates")]
[Authorize]
[Produces("application/json")]
public class AdminBlogTemplateController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITicketCurrentUserService _currentUser;

    public AdminBlogTemplateController(IMediator mediator, ITicketCurrentUserService currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    /// <summary>Danh sách templates (Staff/Manager/Admin).</summary>
    [HttpGet]
    [Authorize(Roles = "Staff,Manager,Admin")]
    [ProducesResponseType(typeof(CommonResponse<List<BlogTemplateDTO>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] GetBlogTemplateListQuery query, CancellationToken ct)
        => Ok(await _mediator.Send(query, ct));

    /// <summary>Chi tiết template (Staff/Manager/Admin).</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Staff,Manager,Admin")]
    [ProducesResponseType(typeof(CommonResponse<BlogTemplateDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new GetBlogTemplateByIdQuery { TemplateId = id }, ct));

    /// <summary>Tạo template mới (Admin only).</summary>
    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(CommonResponse<BlogTemplateDTO>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateBlogTemplateCommand command, CancellationToken ct)
    {
        command.CurrentUserId = GetCurrentUserId();
        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Cập nhật template (Admin only).</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(CommonResponse<BlogTemplateDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateBlogTemplateCommand command, CancellationToken ct)
    {
        command.TemplateId = id;
        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Xóa template (Admin only).</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(CommonResponse<BlogTemplateDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new DeleteBlogTemplateCommand { TemplateId = id }, ct);
        return StatusCode(result.StatusCode, result);
    }

    private Guid GetCurrentUserId() =>
        string.IsNullOrEmpty(_currentUser.UserId) ? Guid.Empty : Guid.Parse(_currentUser.UserId);
}
