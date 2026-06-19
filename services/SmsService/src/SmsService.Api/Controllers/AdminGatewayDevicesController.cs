using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharedContracts.Common.Responses;
using SmsService.Application.CQRS.Command.Admin;
using SmsService.Application.CQRS.Command.Sms;
using SmsService.Application.CQRS.Query.Admin;
using SmsService.Application.DTOs.Response.Admin;

namespace SmsService.Api.Controllers;

/// <summary>
/// Nhóm endpoint quản lý SMS Gateway dành cho <b>Admin</b> — cấp/thu hồi device gateway (Flutter app)
/// và can thiệp vào hàng đợi SMS (huỷ những tin chưa gửi). Đây là kênh internal-only, KHÔNG expose
/// cho end-user.
/// <para>
/// <b>Authentication:</b> JWT Bearer của AuthService (scheme mặc định <c>JwtBearerDefaults.AuthenticationScheme</c>).
/// Explicit chỉ định scheme để tránh nhầm với scheme tuỳ chỉnh <c>"GatewayApiKey"</c> dành cho Flutter.
/// </para>
/// <para>
/// <b>Authorization:</b> Yêu cầu role <c>Admin</c> trong JWT claim. Manager/Staff/Customer KHÔNG được phép.
/// </para>
/// <para>
/// <b>Workflow điển hình:</b>
/// <list type="number">
///   <item>Admin tạo device qua <c>POST /devices</c> → copy <c>apiKey</c> plaintext (chỉ trả 1 lần) → cấu hình vào Flutter app.</item>
///   <item>Theo dõi danh sách device qua <c>GET /devices</c> — biết device nào còn online (<c>LastSeenAt</c>), đã gửi bao nhiêu SMS hôm nay.</item>
///   <item>Khi device thất lạc / bị compromised → <c>DELETE /devices/{id}</c> revoke ngay (idempotent).</item>
///   <item>Khi cần huỷ 1 SMS trước khi device gửi (vd OTP gửi nhầm) → <c>POST /messages/{id}/cancel</c>.</item>
/// </list>
/// </para>
/// </summary>
[ApiController]
[Route("api/admin/sms-gateway")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
public class AdminGatewayDevicesController : ControllerBase
{
    private readonly IMediator _mediator;

    public AdminGatewayDevicesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Tạo gateway device mới — backend sinh API key plaintext 32 byte random base64, trả về <b>1 lần duy nhất</b>.
    /// </summary>
    /// <remarks>
    /// Đây là bước đầu tiên khi onboard một SIM device mới vào hệ thống. Sau khi tạo, admin copy <c>apiKey</c>
    /// trong response vào cấu hình Flutter app (Settings screen) cùng với <c>deviceCode</c>.
    ///
    /// Cách hoạt động:
    /// - Backend sinh <c>apiKey</c> = <c>Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))</c> (256-bit entropy).
    /// - Hash qua BCrypt (workFactor 11) → lưu vào DB column <c>api_key_hash</c>.
    /// - Trả về plaintext <b>1 lần duy nhất</b> trong response — request <c>GET /devices</c> sau này chỉ trả hash, KHÔNG bao giờ trả plaintext.
    /// - Mỗi device có <c>DailyLimit</c> riêng (mặc định 100/ngày, admin có thể override per device khi tạo).
    ///
    /// Body request:
    /// - <c>DeviceName</c>: tên hiển thị (vd "iPhone Văn phòng tầng 5"), max 64 ký tự.
    /// - <c>DeviceCode</c>: định danh duy nhất (vd "android-gateway-001"), max 64 ký tự, dùng trong header <c>X-Device-Code</c>.
    /// - <c>DailyLimit</c>: số SMS tối đa/ngày (default 100, range [1..10000]).
    ///
    /// Lưu ý bảo mật:
    /// - <b>QUAN TRỌNG:</b> Nếu admin mất API key plaintext → BUỘC phải revoke device cũ + tạo device mới (không có cách reset).
    /// - Endpoint này không log API key plaintext vào file (chỉ trả qua HTTPS response).
    /// - Recommend cấu hình HSM/Vault để lưu apiKey production thay vì .env file.
    /// </remarks>
    /// <param name="cmd">Thông tin device cần tạo.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <returns>Device Id + DeviceCode + ApiKey plaintext (hiển thị 1 lần duy nhất).</returns>
    /// <response code="201">Created. Response chứa <c>apiKey</c> plaintext — admin copy ngay, không có request thứ 2 trả lại.</response>
    /// <response code="400">Validation lỗi (field thiếu / quá dài / DailyLimit ngoài range) — ListErrors có chi tiết Field + Detail.</response>
    /// <response code="401">Chưa đăng nhập hoặc JWT hết hạn.</response>
    /// <response code="403">JWT không có role <c>Admin</c>.</response>
    /// <response code="409">DeviceCode đã tồn tại (xung đột business rule — DeviceCode phải unique).</response>
    [HttpPost("devices")]
    [ProducesResponseType(typeof(CommonResponse<CreateGatewayDeviceResponseDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(CommonResponse<CreateGatewayDeviceResponseDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(CommonResponse<CreateGatewayDeviceResponseDto>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(CommonResponse<CreateGatewayDeviceResponseDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<CreateGatewayDeviceResponseDto>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateGatewayDeviceCommand cmd, CancellationToken ct)
    {
        var resp = await _mediator.Send(cmd, ct);
        return StatusCode(resp.StatusCode == 0 ? 200 : resp.StatusCode, resp);
    }

    /// <summary>
    /// List toàn bộ gateway device — kèm trạng thái (active/revoked), counter daily, last seen.
    /// </summary>
    /// <remarks>
    /// Endpoint dùng cho dashboard Admin monitoring:
    /// - Xem device nào còn online (<c>LastSeenAt &lt; 10 phút</c> trước = online).
    /// - Xem device đã gửi bao nhiêu SMS hôm nay (counter daily, reset lúc 00:00 UTC).
    /// - Xem lịch sử revoke (RevokedAt) để audit.
    ///
    /// Mặc định trả về <b>cả device đã revoke</b> để admin xem lịch sử. Truyền <c>?includeRevoked=false</c>
    /// để lọc chỉ device còn active.
    ///
    /// Response KHÔNG bao giờ chứa <c>ApiKeyHash</c> hay plaintext (security).
    /// </remarks>
    /// <param name="includeRevoked">Mặc định <c>true</c> (include cả device đã revoke). Set <c>false</c> để chỉ list active devices.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <returns>Danh sách <see cref="GatewayDeviceDto"/> sắp xếp theo <c>CreatedAt DESC</c>.</returns>
    /// <response code="200">OK. Trả về danh sách (có thể rỗng nếu chưa có device nào).</response>
    /// <response code="401">Chưa đăng nhập hoặc JWT hết hạn.</response>
    /// <response code="403">JWT không có role <c>Admin</c>.</response>
    [HttpGet("devices")]
    [ProducesResponseType(typeof(CommonResponse<List<GatewayDeviceDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<List<GatewayDeviceDto>>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(CommonResponse<List<GatewayDeviceDto>>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List([FromQuery] bool includeRevoked = true, CancellationToken ct = default)
    {
        var resp = await _mediator.Send(new ListGatewayDevicesQuery { IncludeRevoked = includeRevoked }, ct);
        return StatusCode(resp.StatusCode == 0 ? 200 : resp.StatusCode, resp);
    }

    /// <summary>
    /// Thu hồi (revoke) 1 gateway device — set <c>IsActive = false</c>, idempotent.
    /// </summary>
    /// <remarks>
    /// Dùng khi:
    /// - Device thất lạc / bị compromised → revoke ngay để chặn việc gửi SMS spam.
    /// - Device không còn dùng nữa (vd nhân viên nghỉ việc, đổi máy mới).
    /// - Rotate API key: revoke device cũ → tạo device mới.
    ///
    /// Cách hoạt động:
    /// - Set <c>IsActive = false</c>, <c>RevokedAt = now</c>.
    /// - <b>KHÔNG xóa record</b> — giữ lại để audit (xem lịch sử bao nhiêu SMS đã gửi qua device này).
    /// - Sau khi revoke, mọi request từ device đó sẽ trả 403 (auth handler check <c>IsActive &amp;&amp; !IsDeleted</c>).
    /// - SMS đang ở state <c>Sending</c> mà device đã claim sẽ tự revert về <c>Pending</c> sau 5 phút (qua <c>StaleSmsReaperBackgroundService</c>) → device khác có thể pick lại.
    ///
    /// Idempotent: gọi DELETE 2 lần trên cùng device đã revoke vẫn trả 200 (không lỗi).
    ///
    /// Lưu ý:
    /// - <c>DeviceId</c> chỉ được lấy từ route param <c>{id:guid}</c>, KHÔNG bind từ body — bảo vệ bởi <c>[JsonIgnore]</c>.
    /// - Để khôi phục device đã revoke: hiện không có endpoint un-revoke. Phải tạo device mới + cấu hình apiKey mới vào Flutter.
    /// </remarks>
    /// <param name="id">Guid của device cần revoke (route param).</param>
    /// <param name="ct">Token hủy request.</param>
    /// <returns>DeviceCode của device đã revoke.</returns>
    /// <response code="200">OK — device đã revoke (hoặc đã revoke từ trước, idempotent).</response>
    /// <response code="401">Chưa đăng nhập hoặc JWT hết hạn.</response>
    /// <response code="403">JWT không có role <c>Admin</c>.</response>
    /// <response code="404">DeviceId không tồn tại hoặc đã bị soft-delete.</response>
    [HttpDelete("devices/{id:guid}")]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
    {
        var resp = await _mediator.Send(new RevokeGatewayDeviceCommand { DeviceId = id }, ct);
        return StatusCode(resp.StatusCode == 0 ? 200 : resp.StatusCode, resp);
    }

    /// <summary>
    /// Huỷ (cancel) 1 SMS đang ở queue trước khi device gửi — chỉ valid khi state còn ở <c>Pending</c> hoặc <c>Sending</c>.
    /// </summary>
    /// <remarks>
    /// Dùng cho các tình huống admin can thiệp khẩn cấp:
    /// - User báo cáo OTP gửi nhầm → admin cancel SMS Pending để khỏi gửi.
    /// - Phát hiện spam attack → cancel hàng loạt SMS Pending từ source bị compromised.
    /// - Test/staging: clear queue trước khi chạy E2E test mới.
    ///
    /// Cách hoạt động:
    /// - Load SMS từ DB; nếu state đã <c>Sent</c>/<c>Failed</c>/<c>Cancelled</c> (terminal) → trả 409 Conflict.
    /// - Nếu OK: set state <c>Cancelled</c>, append audit log.
    /// - Nếu SMS đang ở state <c>Sending</c> (đã được device claim) → backend push <c>BatchRevoked</c> qua SignalR
    ///   tới device để remove khỏi local queue (đề phòng device chưa gửi qua SIM).
    ///
    /// State machine:
    /// <list type="bullet">
    ///   <item><c>Pending</c> → <c>Cancelled</c> ✅ — handler cho phép.</item>
    ///   <item><c>Sending</c> → <c>Cancelled</c> ✅ — push BatchRevoked tới device đã claim.</item>
    ///   <item><c>Sent</c> → 409 — không thể recall SMS đã gửi qua SIM.</item>
    ///   <item><c>Failed</c> → 409 — đã exhaust retry, cancel vô nghĩa.</item>
    ///   <item><c>Cancelled</c> → 409 — đã cancel từ trước (idempotent fail).</item>
    /// </list>
    ///
    /// Lưu ý:
    /// - <c>SmsId</c> chỉ lấy từ route param <c>{id:guid}</c>, KHÔNG bind từ body — bảo vệ bởi <c>[JsonIgnore]</c>.
    /// - SignalR <c>BatchRevoked</c> là best-effort: nếu device offline, nó sẽ NHẬN khi reconnect (state đã Cancelled).
    /// </remarks>
    /// <param name="id">Guid của SMS cần huỷ (route param).</param>
    /// <param name="ct">Token hủy request.</param>
    /// <returns>State hiện tại của SMS (sẽ là "Cancelled" nếu thành công).</returns>
    /// <response code="200">OK — SMS đã chuyển sang state <c>Cancelled</c>.</response>
    /// <response code="401">Chưa đăng nhập hoặc JWT hết hạn.</response>
    /// <response code="403">JWT không có role <c>Admin</c>.</response>
    /// <response code="404">SmsId không tồn tại hoặc đã bị soft-delete.</response>
    /// <response code="409">SMS đang ở state terminal (<c>Sent</c>/<c>Failed</c>/<c>Cancelled</c>) — không thể cancel.</response>
    [HttpPost("messages/{id:guid}/cancel")]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        var resp = await _mediator.Send(new CancelSmsCommand { SmsId = id }, ct);
        return StatusCode(resp.StatusCode == 0 ? 200 : resp.StatusCode, resp);
    }
}
