using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NotificationService.Application.CQRS.Command.Preference;
using NotificationService.Application.CQRS.Query.Preference;
using NotificationService.Application.DTOs.Response.Preference;
using SharedContracts.Common.Responses;

namespace NotificationService.Api.Controllers;

/// <summary>
/// Quản lý cài đặt thông báo của user (kênh gửi, quiet hours).
/// </summary>
[ApiController]
[Route("api/notification-preferences")]
[Produces("application/json")]
[Authorize]
public class PreferencesController : ControllerBase
{
    private readonly IMediator _mediator;

    public PreferencesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Lấy cài đặt thông báo của user hiện tại.
    /// </summary>
    /// <remarks>
    /// **Quyền:** mọi user đã đăng nhập.
    ///
    /// Nếu user chưa cấu hình, trả về giá trị mặc định (push=true, email=true, sms=false, inApp=true, không có quiet hours).
    /// </remarks>
    /// <response code="200">Trả về preference hiện tại (hoặc default).</response>
    /// <response code="400">Thiếu claim UserId trong JWT.</response>
    /// <response code="401">Chưa đăng nhập / token hết hạn.</response>
    [HttpGet]
    [ProducesResponseType(typeof(NotificationPreferenceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyPreference()
    {
        if (!TryGetUserId(out var userId))
            return BadRequest(new CommonResponse<object> { IsSuccess = false, Message = "Không xác định được UserId từ token." });

        var result = await _mediator.Send(new GetNotificationPreferenceQuery { UserId = userId });
        return Ok(result);
    }

    /// <summary>
    /// Cập nhật cài đặt thông báo của user hiện tại.
    /// </summary>
    /// <remarks>
    /// **Quyền:** mọi user đã đăng nhập.
    ///
    /// - `quietHoursStart` / `quietHoursEnd`: định dạng `"HH:mm"` (vd `"22:00"`, `"07:00"`). Null = tắt quiet hours.
    /// - Quiet hours 22:00–07:00 (qua đêm) được hỗ trợ.
    /// - Critical notifications (EnvironmentalIncident, SlaBreached, IncidentDeclared, …) **luôn được gửi** bất kể quiet hours.
    /// </remarks>
    /// <response code="200">Cập nhật thành công — trả về preference mới.</response>
    /// <response code="400">Validation lỗi (format HH:mm sai, timezone trống).</response>
    /// <response code="401">Chưa đăng nhập / token hết hạn.</response>
    [HttpPut]
    [ProducesResponseType(typeof(NotificationPreferenceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(NotificationPreferenceResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdateMyPreference([FromBody] UpdateNotificationPreferenceCommand cmd)
    {
        if (!TryGetUserId(out var userId))
            return BadRequest(new NotificationPreferenceResponse { IsSuccess = false, Message = "Không xác định được UserId từ token." });

        cmd.UserId = userId;
        var result = await _mediator.Send(cmd);

        return result.IsSuccess ? Ok(result) : BadRequest(result);
    }

    private bool TryGetUserId(out Guid userId)
    {
        var raw = User.FindFirstValue("UserId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out userId);
    }
}
