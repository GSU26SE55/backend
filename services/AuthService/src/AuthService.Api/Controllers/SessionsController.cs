using AuthService.Application.CQRS.Command.Session;
using AuthService.Application.CQRS.Query.Session;
using AuthService.Application.DTOs.Response.RefreshToken;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthService.Api.Controllers;

/// <summary>
/// Quản lý phiên đăng nhập (refresh token) của user.
/// Cho phép user xem các thiết bị/trình duyệt đang đăng nhập, thu hồi 1 phiên cụ thể,
/// hoặc đăng xuất tất cả thiết bị (như tính năng "Log out from all devices" của Google/Facebook).
/// </summary>
[ApiController]
[Route("api/sessions")]
[Produces("application/json")]
[Authorize]
public class SessionsController : ControllerBase
{
    private readonly IMediator _mediator;

    public SessionsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Lấy danh sách session của tài khoản hiện tại.
    /// </summary>
    /// <remarks>
    /// **Quyền truy cập:** mọi user đã đăng nhập (chỉ thấy session của chính mình).
    ///
    /// Mỗi session ứng với 1 lần login (1 refresh token). Trả về metadata gồm IP, User-Agent,
    /// DeviceId, thời điểm cấp/hết hạn, trạng thái (Active/Used/Revoked/Expired/Compromised),
    /// và lý do bị thu hồi nếu có.
    ///
    /// **Tham số:**
    /// - `activeOnly = true` (default): chỉ trả về session đang hoạt động (`Status = Active`
    ///   và chưa hết hạn). Phù hợp cho UI "Thiết bị đang đăng nhập".
    /// - `activeOnly = false`: trả về toàn bộ lịch sử session (gồm cả đã hết hạn / bị revoke).
    ///   Hữu ích cho audit log.
    ///
    /// Sắp xếp theo `IssuedAt` giảm dần (session mới nhất lên đầu).
    /// </remarks>
    /// <param name="activeOnly">Chỉ lấy session đang hoạt động? Mặc định `true`.</param>
    /// <response code="200">Danh sách `SessionDto`.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    [HttpGet("me")]
    [ProducesResponseType(typeof(SessionListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SessionListResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMySessions(
        [FromQuery] bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetMySessionsQuery { ActiveOnly = activeOnly }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Thu hồi (revoke) 1 session cụ thể — đăng xuất thiết bị đó.
    /// </summary>
    /// <remarks>
    /// **Quyền truy cập:** chỉ user sở hữu session đó.
    ///
    /// **Use case:** user thấy 1 thiết bị lạ trong danh sách `GET /me` và muốn ngắt quyền truy cập
    /// của thiết bị đó ngay lập tức.
    ///
    /// **Hành vi:**
    /// - Set `Status = Revoked`, `RevokedAt = now`, `RevokedReason = "User revoked"`.
    /// - User trên thiết bị đó sẽ KHÔNG thể dùng refresh token này để xin access token mới.
    /// - Tuy nhiên access token hiện tại (TTL 1h) vẫn còn hiệu lực cho đến khi hết hạn —
    ///   đây là tradeoff đặc thù của JWT stateless. Nếu cần ngắt ngay, kết hợp với việc đổi status account.
    ///
    /// **Bảo mật:** nếu `SessionId` thuộc về user khác → trả **403** thay vì **404** để không
    /// tiết lộ session đó tồn tại.
    /// </remarks>
    /// <param name="id">Id của session (refresh token).</param>
    /// <response code="200">Revoke thành công, hoặc session vốn đã không còn Active.</response>
    /// <response code="403">Session không thuộc về user hiện tại.</response>
    /// <response code="404">Không tìm thấy session.</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(SessionActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SessionActionResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(SessionActionResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new RevokeSessionCommand { SessionId = id }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Đăng xuất tất cả thiết bị (revoke tất cả refresh token đang Active).
    /// </summary>
    /// <remarks>
    /// **Quyền truy cập:** mọi user đã đăng nhập.
    ///
    /// **Use case:** user đổi mật khẩu, nghi ngờ tài khoản bị xâm nhập, hoặc đơn giản muốn
    /// "log out everywhere".
    ///
    /// **Body request:**
    /// - `ExceptCurrent = true` (default): KHÔNG revoke session hiện tại — user vẫn duy trì login
    ///   trên thiết bị đang gọi API. Cần kèm `CurrentRefreshToken` để hệ thống biết session nào
    ///   là "hiện tại" (refresh token là string ngẫu nhiên, không có trong access token).
    /// - `ExceptCurrent = false`: revoke TẤT CẢ kể cả session hiện tại.
    ///
    /// **Lưu ý:** số lượng session bị revoke trả về trong `Data`. Như endpoint Revoke đơn lẻ,
    /// access token đã cấp vẫn còn hiệu lực cho đến khi hết TTL (1h).
    /// </remarks>
    /// <param name="command">Tùy chọn loại trừ session hiện tại + giá trị refresh token hiện tại.</param>
    /// <response code="200">Trả về số session đã thu hồi trong `Data`.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    [HttpPost("revoke-all")]
    [ProducesResponseType(typeof(SessionActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SessionActionResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RevokeAll([FromBody] RevokeAllSessionsCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}
