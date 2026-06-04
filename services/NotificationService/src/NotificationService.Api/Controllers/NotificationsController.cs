using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NotificationService.Application.CQRS.Command.Notification;
using NotificationService.Application.CQRS.Query.Notification;
using NotificationService.Application.DTOs.Response.Notification;
using NotificationService.Domain.Enums;

namespace NotificationService.Api.Controllers;

/// <summary>
/// Module quản lý notification của người dùng.
/// </summary>
[ApiController]
[Route("api/notifications")]
[Produces("application/json")]
public class NotificationsController : ControllerBase
{
    private readonly IMediator _mediator;

    public NotificationsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Lấy danh sách notification của user hiện tại (lấy UserId từ JWT claim).
    /// </summary>
    /// <remarks>
    /// **Quyền:** mọi user đã đăng nhập.
    ///
    /// **Filter (optional):**
    /// - `type`: loại notification (TicketCreated=1, TicketAssigned=2, …).
    /// - `channel`: 1=Push, 2=Email, 3=Sms, 4=InApp.
    /// - `status`: 1=Pending, 2=Sent, 3=Failed, 4=Read.
    /// - `unreadOnly=true`: chỉ lấy notification chưa đọc.
    ///
    /// Sắp xếp theo `CreatedAt` giảm dần.
    /// </remarks>
    [HttpGet]
    [Authorize]
    [ProducesResponseType(typeof(NotificationListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyNotifications(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] NotificationTypeEnum? type = null,
        [FromQuery] NotificationChannelEnum? channel = null,
        [FromQuery] NotificationStatusEnum? status = null,
        [FromQuery] bool? unreadOnly = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized(new { isSuccess = false, message = "Không xác định được user." });

        var query = new GetNotificationsQuery
        {
            UserId = userId,
            PageNumber = pageNumber,
            PageSize = pageSize,
            Type = type,
            Channel = channel,
            Status = status,
            UnreadOnly = unreadOnly
        };

        var result = await _mediator.Send(query, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Tạo 1 notification mới (admin/test endpoint).
    /// </summary>
    /// <remarks>
    /// **Quyền:** Admin.
    ///
    /// Production flow chính tạo notification qua RabbitMQ consumer (TicketCreated,
    /// BatteryAnomalyDetected, …) — endpoint này dùng cho test và backfill thủ công.
    /// </remarks>
    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(NotificationActionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(NotificationActionResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] CreateNotificationCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    private bool TryGetCurrentUserId(out Guid userId)
    {
        var raw = User.FindFirstValue("UserId")
                  ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue("sub");

        return Guid.TryParse(raw, out userId);
    }
}
