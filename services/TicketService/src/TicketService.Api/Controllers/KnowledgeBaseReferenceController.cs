using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.TicketKbReferences;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Enums;

namespace TicketService.Api.Controllers;

/// <summary>
/// Controller dành cho Staff/Manager/Admin để gán bài viết Knowledge Base vào Ticket.
/// </summary>
[ApiController]
[Route("api/knowledge-base/references")]
[Authorize(Roles = "Staff,Manager,Admin")]
[Produces("application/json")]
public class KnowledgeBaseReferenceController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITicketCurrentUserService _currentUser;

    public KnowledgeBaseReferenceController(IMediator mediator, ITicketCurrentUserService currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Gán một bài viết Knowledge Base vào một Ticket cụ thể làm tài liệu tham khảo.
    /// </summary>
    /// <param name="command">Thông tin gán (TicketId, KbArticleId, ReferenceType, Note).</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Gán thành công.</response>
    /// <response code="404">Không tìm thấy Ticket hoặc Bài viết.</response>
    [HttpPost]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddReference([FromBody] AddTicketKbReferenceCommand command, CancellationToken ct)
    {
        command.CurrentUserId = GetCurrentUserId();
        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    private Guid GetCurrentUserId()
    {
        return string.IsNullOrEmpty(_currentUser.UserId) ? Guid.Empty : Guid.Parse(_currentUser.UserId);
    }
}
