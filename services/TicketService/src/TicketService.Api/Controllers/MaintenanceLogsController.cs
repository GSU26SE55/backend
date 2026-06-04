using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketService.Application.CQRS.Commands.MaintenanceLogAdd;
using TicketService.Application.DTOs.Response.Ticket;
using TicketService.Domain.Enums;

namespace TicketService.Api.Controllers;

[ApiController]
[Route("api/v1/tickets/{ticketId}/maintenance-logs")]
[Authorize(Roles = "Staff,Manager,Admin")]
[Produces("application/json")]
public class MaintenanceLogsController : ControllerBase
{
    private readonly IMediator _mediator;

    public MaintenanceLogsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Thêm nhật ký bảo trì cho Ticket.
    /// </summary>
    /// <param name="ticketId">ID của Ticket.</param>
    /// <param name="request">Thông tin nhật ký bảo trì.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <returns>Kết quả thực hiện.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddLog(Guid ticketId, [FromBody] MaintenanceLogAddRequest request, CancellationToken ct)
    {
        var command = new MaintenanceLogAddCommand(
            ticketId,
            GetUserId(),
            request.LogType,
            request.Summary,
            request.DiagnosisDetails,
            request.ActionsTaken,
            request.DurationMinutes,
            request.ResolutionNote,
            request.StartedAt,
            request.CompletedAt,
            request.PartsUsed,
            request.Attachments?.Select(a => new MaintenanceAttachmentInput(
                a.FileId, a.FileName, a.ContentType, a.SizeBytes)).ToList(),
            request.BeforePhotos?.Select(a => new MaintenanceAttachmentInput(
                a.FileId, a.FileName, a.ContentType, a.SizeBytes)).ToList(),
            request.AfterPhotos?.Select(a => new MaintenanceAttachmentInput(
                a.FileId, a.FileName, a.ContentType, a.SizeBytes)).ToList(),
            request.RelatedKbArticleIds,
            request.CheckInLatitude,
            request.CheckInLongitude,
            request.CheckInAt
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
}

public class MaintenanceLogAddRequest
{
    public MaintenanceLogTypeEnum LogType { get; set; }
    public required string Summary { get; set; }
    public string? DiagnosisDetails { get; set; }
    public string? ActionsTaken { get; set; }
    public int DurationMinutes { get; set; }
    public string? ResolutionNote { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? PartsUsed { get; set; }
    public List<MaintenanceAttachmentRequest>? Attachments { get; set; }
    public List<MaintenanceAttachmentRequest>? BeforePhotos { get; set; }
    public List<MaintenanceAttachmentRequest>? AfterPhotos { get; set; }
    public List<Guid>? RelatedKbArticleIds { get; set; }
    public decimal? CheckInLatitude { get; set; }
    public decimal? CheckInLongitude { get; set; }
    public DateTime? CheckInAt { get; set; }
}

public class MaintenanceAttachmentRequest
{
    public Guid FileId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
}
