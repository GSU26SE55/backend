using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NotificationService.Application.CQRS.Command.Preference;
using NotificationService.Application.CQRS.Query.Preference;
using NotificationService.Application.DTOs.Response.Preference;
using NotificationService.Domain.Enums;
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
            return BadRequest(new CommonResponse<object> { IsSuccess = false, Message = "Unable to determine the UserId from the token." });

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
            return BadRequest(new NotificationPreferenceResponse { IsSuccess = false, Message = "Unable to determine the UserId from the token." });

        cmd.UserId = userId;
        var result = await _mediator.Send(cmd);

        return result.IsSuccess ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// Sprint 6.3 NOTI3-04 (#704) — lấy ma trận tuỳ chọn **nhóm × kênh**.
    /// </summary>
    /// <remarks>
    /// **Quyền:** mọi user đã đăng nhập.
    ///
    /// Trước sprint này người dùng chỉ bật/tắt được cả kênh — tắt Email là mất luôn email SLA sắp vỡ.
    /// Endpoint này cho phép chọn theo 6 nhóm nghiệp vụ (Ticket · SLA · Battery · Environmental ·
    /// Chat · Account).
    ///
    /// Luôn trả **đủ 6 nhóm**. Nhóm chưa tuỳ chỉnh có `isCustomized=false` và mang giá trị kế thừa
    /// từ công tắc kênh toàn cục.
    ///
    /// Quan hệ hai cấp là **và logic**: kênh phải bật ở cả `channels` lẫn dòng nhóm thì mới gửi.
    /// `GET /api/notification-preferences` (bản cũ) vẫn giữ nguyên để FE hiện tại không vỡ.
    /// </remarks>
    /// <response code="200">Trả về ma trận hiện tại.</response>
    /// <response code="400">Thiếu claim UserId trong JWT.</response>
    /// <response code="401">Chưa đăng nhập / token hết hạn.</response>
    [HttpGet("matrix")]
    [ProducesResponseType(typeof(NotificationPreferenceMatrixResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyPreferenceMatrix()
    {
        if (!TryGetUserId(out var userId))
            return BadRequest(new CommonResponse<object> { IsSuccess = false, Message = "Unable to determine the UserId from the token." });

        var result = await _mediator.Send(new GetNotificationPreferenceMatrixQuery { UserId = userId });
        return Ok(result);
    }

    /// <summary>
    /// Sprint 6.3 NOTI3-04 (#704) — cập nhật ma trận tuỳ chọn **nhóm × kênh**.
    /// </summary>
    /// <remarks>
    /// **Quyền:** mọi user đã đăng nhập.
    ///
    /// Ghi theo kiểu **vá từng dòng**: chỉ nhóm có trong `items` bị đổi, nhóm còn lại giữ nguyên
    /// (ghi đè toàn bộ sẽ khiến hai tab mở song song xoá thiết lập của nhau).
    ///
    /// Gửi trùng cùng một nhóm trong một request → **400**.
    /// </remarks>
    /// <response code="200">Cập nhật thành công — trả về ma trận mới.</response>
    /// <response code="400">Nhóm không hợp lệ, trùng nhóm, hoặc danh sách rỗng.</response>
    /// <response code="401">Chưa đăng nhập / token hết hạn.</response>
    [HttpPut("matrix")]
    [ProducesResponseType(typeof(NotificationPreferenceMatrixResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(NotificationPreferenceMatrixResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdateMyPreferenceMatrix(
        [FromBody] UpdateNotificationCategoryPreferenceCommand cmd)
    {
        if (!TryGetUserId(out var userId))
            return BadRequest(new NotificationPreferenceMatrixResponse
            {
                IsSuccess = false,
                Message = "Unable to determine the UserId from the token."
            });

        cmd.UserId = userId;
        var result = await _mediator.Send(cmd);

        return result.IsSuccess ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// Sprint 6.3 NOTI3-04 (#704) — bảng tra cứu <c>NotificationTypeEnum</c> → nhóm.
    /// </summary>
    /// <remarks>
    /// **Quyền:** mọi user đã đăng nhập.
    ///
    /// FE dùng để giải thích "tắt nhóm này thì mất những thông báo nào", mà không phải nhân bản
    /// bảng ánh xạ ở phía client (nhân bản là chắc chắn sẽ lệch khi thêm type mới).
    /// </remarks>
    /// <response code="200">Trả về ánh xạ type → nhóm.</response>
    [HttpGet("categories")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetCategoryMap()
    {
        var items = NotificationCategoryMap.All
            .OrderBy(kv => (int)kv.Value)
            .ThenBy(kv => (int)kv.Key)
            .Select(kv => new
            {
                type = kv.Key.ToString(),
                typeValue = (int)kv.Key,
                category = kv.Value.ToString(),
                categoryValue = (int)kv.Value,
            });

        return Ok(new CommonResponse<object> { IsSuccess = true, Data = items });
    }

    private bool TryGetUserId(out Guid userId)
    {
        var raw = User.FindFirstValue("UserId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out userId);
    }
}
