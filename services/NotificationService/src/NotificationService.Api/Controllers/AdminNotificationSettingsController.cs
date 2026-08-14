using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NotificationService.Application.CQRS.Command.Setting;
using NotificationService.Application.CQRS.Query.Setting;
using NotificationService.Application.DTOs.Response.Setting;

namespace NotificationService.Api.Controllers;

/// <summary>
/// Cấu hình cấp hệ thống của NotificationService, sửa được lúc chạy từ màn hình Admin.
/// </summary>
/// <remarks>
/// Dùng <c>[Authorize(Roles = "Admin")]</c> chứ KHÔNG dùng policy <c>AdminOnly</c>: policy đó tồn
/// tại trong cấu hình nhưng không service nào đăng ký, nên gắn vào sẽ chặn cả Admin thật. Đây là
/// quy ước chung của 4 controller admin còn lại trong service này.
/// </remarks>
[ApiController]
[Route("api/admin/notification-settings")]
[Produces("application/json")]
[Authorize(Roles = "Admin")]
public class AdminNotificationSettingsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AdminNotificationSettingsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Đường vận chuyển push đang áp dụng, kèm toàn bộ lựa chọn hợp lệ để dựng ô chọn.
    /// </summary>
    /// <remarks>
    /// **Quyền:** Admin.
    ///
    /// Trả về cả `options[]` để frontend không phải hard-code danh sách — thêm đường vận chuyển mới
    /// ở backend là giao diện tự có thêm lựa chọn.
    ///
    /// Giá trị này là cấu hình HỆ THỐNG, khác với `PUT /api/notification-preferences` là tuỳ chọn
    /// của từng người dùng. Người dùng tắt kênh Push thì không nhận push bằng đường nào cả.
    /// </remarks>
    /// <response code="200">Trả về transport hiện tại.</response>
    /// <response code="401">Chưa đăng nhập / token hết hạn.</response>
    /// <response code="403">Không phải Admin.</response>
    [HttpGet("push-transport")]
    [ProducesResponseType(typeof(PushTransportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPushTransport(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetPushTransportQuery(), cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Đổi đường vận chuyển push cho toàn hệ thống.
    /// </summary>
    /// <remarks>
    /// **Quyền:** Admin.
    ///
    /// - `1` — **SignalR**: chỉ hub của hệ thống. Không cần khoá EAS/FCM, không cần device token.
    /// - `2` — **Expo**: chỉ Expo Push API. Cần device token còn hoạt động; có đối soát biên nhận
    ///   nên thông báo mới lên được `Delivered`.
    /// - `3` — **Both**: gửi cả hai, thành công khi ít nhất một đường thành công.
    ///
    /// Có hiệu lực ngay với tiến trình xử lý request này; các tiến trình khác (worker nền, replica)
    /// nhận giá trị mới chậm nhất sau `Notification:Push:CacheSeconds` giây.
    ///
    /// Hai worker nền phụ thuộc Expo (đối soát biên nhận, bù SMS cho push critical) tự bật/tắt theo
    /// giá trị này — không cần khởi động lại service.
    /// </remarks>
    /// <response code="200">Đổi thành công (hoặc giá trị mới trùng giá trị cũ).</response>
    /// <response code="400">Transport không hợp lệ.</response>
    /// <response code="401">Chưa đăng nhập / token hết hạn.</response>
    /// <response code="403">Không phải Admin.</response>
    [HttpPut("push-transport")]
    [ProducesResponseType(typeof(PushTransportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(PushTransportResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdatePushTransport(
        [FromBody] UpdatePushTransportCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}
