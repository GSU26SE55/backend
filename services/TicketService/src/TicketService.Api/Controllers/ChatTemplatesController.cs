using System;
using System.Linq;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.ChatTemplates;
using TicketService.Application.CQRS.Query.ChatTemplates;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Api.Controllers;

/// <summary>
/// Quản lý chat template — Staff/Manager/Admin tạo và sử dụng template.
/// </summary>
[ApiController]
[Route("api/chat-templates")]
[Authorize(Roles = "Staff,Manager,Admin")]
[Produces("application/json")]
public class ChatTemplatesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITicketCurrentUserService _currentUser;

    public ChatTemplatesController(IMediator mediator, ITicketCurrentUserService currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Lấy danh sách chat template — Personal của mình + Global + Team.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(CommonResponse<PaginationResponse<ChatTemplateDTO>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetTemplates([FromQuery] ChatTemplatesQuery query)
    {
        query.ActorUserId = GetActorUserId();
        query.ActorRoles = GetCurrentRoles();
        return Ok(await _mediator.Send(query));
    }

    /// <summary>
    /// Lấy chi tiết chat template theo ID.
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(CommonResponse<ChatTemplateDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetTemplateById(Guid id)
    {
        var result = await _mediator.Send(new ChatTemplateGetByIdQuery
        {
            TemplateId = id,
            ActorUserId = GetActorUserId(),
            ActorRoles = GetCurrentRoles()
        });
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Tạo chat template mới.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(CommonResponse<ChatTemplateDTO>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CreateTemplate([FromBody] ChatTemplateCreateCommand command)
    {
        command.ActorUserId = GetActorUserId();
        command.ActorRoles = GetCurrentRoles();
        var result = await _mediator.Send(command);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Cập nhật chat template — chỉ author hoặc Manager/Admin.
    /// </summary>
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(CommonResponse<ChatTemplateDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdateTemplate(Guid id, [FromBody] ChatTemplateUpdateCommand command)
    {
        command.TemplateId = id;
        command.ActorUserId = GetActorUserId();
        command.ActorRoles = GetCurrentRoles();
        var result = await _mediator.Send(command);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Xóa (soft-delete) chat template — chỉ author hoặc Manager/Admin.
    /// </summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DeleteTemplate(Guid id)
    {
        var result = await _mediator.Send(new ChatTemplateDeleteCommand
        {
            TemplateId = id,
            ActorUserId = GetActorUserId(),
            ActorRoles = GetCurrentRoles()
        });
        return StatusCode(result.StatusCode, result);
    }

    private Guid GetActorUserId()
        => Guid.TryParse(_currentUser.UserId, out var uid) ? uid : Guid.Empty;

    private string[] GetCurrentRoles()
        => User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();
}
