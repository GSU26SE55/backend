using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketService.Application.CQRS.Commands.CommentAdd;
using TicketService.Application.DTOs.Response.Ticket;
using TicketService.Domain.Enums;

namespace TicketService.Api.Controllers;

[ApiController]
[Route("api/v1/tickets/{ticketId}/comments")]
[Authorize]
[Produces("application/json")]
public class TicketCommentsController : ControllerBase
{
    private readonly IMediator _mediator;

    public TicketCommentsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Thêm bình luận vào Ticket.
    /// </summary>
    /// <param name="ticketId">ID của Ticket.</param>
    /// <param name="request">Nội dung bình luận và đính kèm.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <returns>Kết quả thực hiện.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddComment(Guid ticketId, [FromBody] CommentAddRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        var roles = GetCurrentRoles();

        var userRole = ActorRoleEnum.Staff; // Default
        if (roles.Contains("Customer"))
            userRole = ActorRoleEnum.Customer;
        else if (roles.Contains("Manager"))
            userRole = ActorRoleEnum.Manager;
        else if (roles.Contains("Admin"))
            userRole = ActorRoleEnum.Admin;

        var command = new CommentAddCommand(
            ticketId,
            userId,
            userRole,
            GetUserName(),
            request.Body,
            request.IsInternal,
            request.Attachments?.Select(a => new CommentAttachmentInput(
                a.FileId, a.FileName, a.ContentType, a.SizeBytes)).ToList()
        );

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    private Guid GetUserId()
    {
        var userIdClaim = User.FindFirst("id")?.Value;
        Guid.TryParse(userIdClaim, out var userId);
        return userId;
    }

    private string GetUserName()
    {
        return User.FindFirst("name")?.Value
               ?? User.FindFirst(ClaimTypes.Name)?.Value
               ?? "Unknown";
    }

    private string[] GetCurrentRoles()
        => User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();
}

public class CommentAddRequest
{
    public required string Body { get; set; }
    public bool IsInternal { get; set; }
    public List<CommentAttachmentRequest>? Attachments { get; set; }
}

public class CommentAttachmentRequest
{
    public Guid FileId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
}
