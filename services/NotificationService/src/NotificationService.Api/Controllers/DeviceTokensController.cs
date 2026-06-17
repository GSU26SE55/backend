using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NotificationService.Application.CQRS.Command.DeviceToken;
using NotificationService.Application.CQRS.Query.DeviceToken;
using NotificationService.Application.DTOs.Response.DeviceToken;
using SharedContracts.Common.Responses;

namespace NotificationService.Api.Controllers;

/// <summary>
/// Quản lý push token thiết bị (Expo/FCM) cho Mobile/Web app. UserId luôn lấy từ JWT —
/// user chỉ thao tác trên token của chính mình.
/// </summary>
[ApiController]
[Route("api/device-tokens")]
[Produces("application/json")]
[Authorize]
public class DeviceTokensController : ControllerBase
{
    private readonly IMediator _mediator;

    public DeviceTokensController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Đăng ký push token của thiết bị hiện tại cho user đang đăng nhập.
    /// </summary>
    /// <remarks>
    /// **Quyền:** mọi user đã đăng nhập.
    ///
    /// - Token mới → tạo record, trả **201**.
    /// - Token đã đăng ký và đang active cho chính user → **409**.
    /// - Token đã từng hủy đăng ký (inactive) hoặc thuộc tài khoản khác trên cùng thiết bị →
    ///   reactivate + gán lại cho user hiện tại, trả **200** (flow re-login).
    /// </remarks>
    /// <response code="201">Đăng ký mới thành công.</response>
    /// <response code="200">Đăng ký lại (reactivate) thành công.</response>
    /// <response code="400">Validation lỗi (token rỗng/quá dài, platform không hợp lệ) hoặc thiếu claim UserId.</response>
    /// <response code="401">Chưa đăng nhập / token hết hạn.</response>
    /// <response code="409">Thiết bị đã được đăng ký và đang active.</response>
    [HttpPost]
    [ProducesResponseType(typeof(DeviceTokenActionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(DeviceTokenActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(DeviceTokenActionResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(DeviceTokenActionResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterDeviceTokenCommand command,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
            return BadRequest(UserNotResolved());

        command.UserId = userId;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Hủy đăng ký push token của thiết bị (logout). Định danh theo token string.
    /// </summary>
    /// <remarks>
    /// **Quyền:** mọi user đã đăng nhập. Chỉ hủy được token đang active thuộc về chính mình
    /// (set `IsActive=false`, giữ record để re-login dễ).
    /// </remarks>
    /// <response code="200">Hủy đăng ký thành công.</response>
    /// <response code="400">Validation lỗi hoặc thiếu claim UserId.</response>
    /// <response code="401">Chưa đăng nhập / token hết hạn.</response>
    /// <response code="404">Không tìm thấy token đang đăng ký của user hiện tại.</response>
    [HttpDelete]
    [ProducesResponseType(typeof(DeviceTokenActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(DeviceTokenActionResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(DeviceTokenActionResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Unregister(
        [FromBody] UnregisterDeviceTokenCommand command,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
            return BadRequest(UserNotResolved());

        command.UserId = userId;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Liệt kê các thiết bị đã đăng ký của user hiện tại (không trả raw token string).
    /// </summary>
    /// <remarks>**Quyền:** mọi user đã đăng nhập. Sắp xếp theo lần dùng gần nhất.</remarks>
    /// <response code="200">Trả danh sách thiết bị (có thể rỗng).</response>
    /// <response code="400">Thiếu claim UserId.</response>
    /// <response code="401">Chưa đăng nhập / token hết hạn.</response>
    [HttpGet]
    [ProducesResponseType(typeof(DeviceTokenListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(DeviceTokenListResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyDevices(CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
            return BadRequest(new DeviceTokenListResponse
            {
                IsSuccess = false,
                StatusCode = StatusCodes.Status400BadRequest,
                Message = "Không xác định được user."
            });

        var result = await _mediator.Send(new GetMyDeviceTokensQuery { UserId = userId }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    private static DeviceTokenActionResponse UserNotResolved() => new()
    {
        IsSuccess = false,
        StatusCode = StatusCodes.Status400BadRequest,
        Message = "Không xác định được user."
    };

    private bool TryGetCurrentUserId(out Guid userId)
    {
        var raw = User.FindFirstValue("UserId")
                  ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue("sub");

        return Guid.TryParse(raw, out userId);
    }
}
