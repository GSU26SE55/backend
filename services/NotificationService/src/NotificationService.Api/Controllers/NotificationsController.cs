using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NotificationService.Application.CQRS.Command.Notification;
using NotificationService.Application.CQRS.Query.Notification;
using NotificationService.Application.DTOs.Response.Notification;
using NotificationService.Domain.Enums;
using SharedContracts.Common.Responses;

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
    /// <response code="200">Trả danh sách notification (có thể rỗng).</response>
    /// <response code="400">JWT đã pass `[Authorize]` nhưng không trích xuất được claim `UserId` (claim missing/malformed trong token). Đây là client request error chứ không phải auth fail.</response>
    /// <response code="401">Chưa đăng nhập / token không hợp lệ (do middleware trả).</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize]
    [ProducesResponseType(typeof(NotificationListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<NotificationListResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
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
            return BadRequest(new CommonResponse<NotificationListResponse>
            {
                IsSuccess = false,
                StatusCode = StatusCodes.Status400BadRequest,
                Message = "Không xác định được user."
            });

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
    /// Tạo 1 notification thủ công (admin/test endpoint) — flow production chính dùng RabbitMQ consumer (BatteryAnomaly/EnvIncident/IotDeviceOffline events). Dùng cho backfill hoặc integration test.
    /// </summary>
    /// <remarks>
    /// **Quyền:** Admin.
    ///
    /// Production flow chính tạo notification qua RabbitMQ consumer:
    /// `AlertTicketSagaFailedConsumer`, `BatteryAlertEscalationRequestedConsumer`,
    /// `IotDeviceWentOfflineConsumer` (xem `NotificationService.Application/Consumers/`).
    /// Endpoint này dùng cho test, backfill thủ công, hoặc các integration event chưa có consumer riêng.
    /// </remarks>
    /// <param name="command">Notification payload — UserId, Type (enum), Channel (Push/Email/Sms/InApp), Title, Body, PayloadJson tự do.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <response code="201">Tạo notification thành công.</response>
    /// <response code="400">Field validation lỗi (UserId rỗng / Type không hợp lệ / Title quá dài / ...).</response>
    /// <response code="401">Chưa đăng nhập / token hết hạn.</response>
    /// <response code="403">Không có role Admin.</response>
    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(NotificationActionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(NotificationActionResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
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
