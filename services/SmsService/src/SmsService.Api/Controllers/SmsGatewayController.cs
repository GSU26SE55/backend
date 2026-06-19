using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SharedContracts.Common.Responses;
using SmsService.Application.CQRS.Command.Sms;
using SmsService.Application.DTOs.Response.Sms;
using SmsService.Infrastructure.Security;

namespace SmsService.Api.Controllers;

/// <summary>
/// Request body cho <c>POST /api/sms-gateway/messages/report</c> — Flutter app báo kết quả gửi 1 SMS.
/// Fields:
/// <list type="bullet">
///   <item><c>SmsId</c>: Guid của SMS được claim qua <c>GET /messages/pending</c>.</item>
///   <item><c>Status</c>: "Sent" | "Failed" (case-insensitive).</item>
///   <item><c>ErrorMessage</c>: optional khi Status = "Failed" — mô tả lý do (timeout, no signal, blocked by carrier…).</item>
/// </list>
/// </summary>
public record ReportSmsRequest(Guid SmsId, string Status, string? ErrorMessage);

/// <summary>
/// Nhóm endpoint cho Flutter app <c>sms_fowarder</c> (Android có SIM thật) — claim batch SMS Pending,
/// báo kết quả gửi, và heartbeat duy trì kết nối. Đây là kênh device→backend, KHÔNG dùng JWT user.
/// <para>
/// <b>Authentication:</b> Scheme tùy chỉnh <c>"GatewayApiKey"</c> — header <c>Authorization: Bearer &lt;api-key-plaintext&gt;</c>
/// (BCrypt hash trong DB, plaintext chỉ hiển thị 1 lần khi admin tạo device qua
/// <c>POST /api/admin/sms-gateway/devices</c>) + header <c>X-Device-Code</c> (định danh device).
/// </para>
/// <para>
/// <b>Realtime:</b> Để nhận push <c>NewPendingSms</c> &lt; 1 giây sau khi SMS được queue, Flutter app cần
/// thêm subscribe SignalR Hub <c>/hubs/sms-gateway</c> (handshake WS dùng query string
/// <c>?deviceCode=...&amp;access_token=...</c>). Polling <c>GET /messages/pending</c> chỉ là fallback.
/// </para>
/// <para>
/// <b>Rate limit:</b> 60 request/phút/device (partition theo header <c>X-Device-Code</c>) — vượt giới hạn trả HTTP 429.
/// </para>
/// </summary>
[ApiController]
[Route("api/sms-gateway")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = GatewayAuthOptions.SchemeName)]
[EnableRateLimiting("gateway")]
public class SmsGatewayController : ControllerBase
{
    private readonly IMediator _mediator;

    public SmsGatewayController(IMediator mediator)
    {
        _mediator = mediator;
    }

    private string DeviceCode => User.FindFirst("device_code")?.Value
        ?? throw new InvalidOperationException("Missing device_code claim.");

    private Guid DeviceId => Guid.Parse(User.FindFirst("device_id")!.Value);

    /// <summary>
    /// Claim 1 batch SMS Pending để device gửi qua SIM — chuyển trạng thái <c>Pending → Sending</c>.
    /// </summary>
    /// <remarks>
    /// Endpoint này là điểm vào chính của luồng polling fallback (nếu device không nhận được SignalR push).
    /// Cũng có thể dùng kết hợp với SignalR: nhận <c>NewPendingSms</c> → trigger claim ngay.
    ///
    /// Cách hoạt động:
    /// - <c>DeviceId</c> + <c>DeviceCode</c> được lấy từ JWT-like claim của scheme <c>GatewayApiKey</c> (controller gán).
    /// - <c>limit</c> bị clamp về <c>[1..20]</c> trong handler để tránh device gửi quá tải.
    /// - Pick những SMS có:
    ///   - <c>Status == Pending</c> AND (<c>TargetDeviceCode</c> = null hoặc khớp device hiện tại), HOẶC
    ///   - <c>Status == Sending</c> AND <c>PickedAt &lt; now − 5 phút</c> (stale claim từ device crashed/timed-out).
    /// - Áp daily limit (<c>SmsGatewayDevice.DailyLimit</c>): nếu hôm nay device đã gửi vượt cap → trả rỗng + Message "Daily limit reached".
    /// - Concurrency token <c>xmin</c> bảo vệ: 2 device cùng claim 1 row → 1 thắng, 1 trả rỗng (KHÔNG fail, lần poll sau retry).
    /// - Sau claim thành công, mỗi SMS append 1 audit log <c>Picked</c>.
    ///
    /// Khi nào trả rỗng (HTTP 200, Data=[]):
    /// - Không còn SMS nào Pending cho device này.
    /// - Daily limit đã hết (Message = "Daily limit reached").
    /// - Concurrency conflict (Message = "Concurrent claim, retry next poll").
    ///
    /// Lưu ý:
    /// - SMS đã claim mà chưa report sau 5 phút sẽ bị <c>StaleSmsReaperBackgroundService</c> revert về Pending — device khác có thể pick lại.
    /// - Mỗi SMS chỉ được tính vào daily limit sau khi device báo <c>Sent</c> qua <c>POST /messages/report</c>.
    /// </remarks>
    /// <param name="limit">Số SMS tối đa mỗi batch (mặc định 5, clamp [1..20]). Khuyến nghị 1-5 cho device yếu, 10-20 cho device mạnh.</param>
    /// <param name="ct">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns>Danh sách SMS đã claim — mỗi item gồm <c>Id</c>, <c>PhoneNumber</c> (E.164), <c>Message</c> (nội dung plaintext).</returns>
    /// <response code="200">OK. Trả về danh sách (có thể rỗng nếu hết Pending / daily limit / concurrency).</response>
    /// <response code="401">Missing/invalid API key hoặc <c>X-Device-Code</c>.</response>
    /// <response code="403">Device không tồn tại hoặc đã bị revoke (<c>IsActive = false</c>).</response>
    /// <response code="429">Vượt rate limit 60 req/phút/device.</response>
    [HttpGet("messages/pending")]
    [ProducesResponseType(typeof(CommonResponse<List<PendingSmsDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<List<PendingSmsDto>>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(CommonResponse<List<PendingSmsDto>>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> GetPending([FromQuery] int limit = 5, CancellationToken ct = default)
    {
        var resp = await _mediator.Send(new ClaimPendingMessagesCommand
        {
            DeviceId = DeviceId,
            DeviceCode = DeviceCode,
            Limit = limit
        }, ct);
        return StatusCode(resp.StatusCode == 0 ? 200 : resp.StatusCode, resp);
    }

    /// <summary>
    /// Báo kết quả gửi 1 SMS (<c>Sent</c> hoặc <c>Failed</c>) — idempotent, có thể gọi nhiều lần an toàn.
    /// </summary>
    /// <remarks>
    /// Sau khi device claim được SMS qua <c>GET /messages/pending</c> và đẩy nội dung qua SIM, gọi endpoint
    /// này để cập nhật trạng thái cuối:
    ///
    /// State machine:
    /// - Chỉ accept khi SMS đang ở state <c>Sending</c> AND <c>GatewayDeviceCode</c> khớp device hiện tại.
    /// - Mọi state khác (<c>Pending</c>/<c>Sent</c>/<c>Failed</c>/<c>Cancelled</c>) → trả 200 với Message "Report ignored" (idempotent no-op để Flutter dừng retry).
    /// - Concurrent xmin conflict → trả 200 với Message "Concurrent report" (Flutter cũng không retry).
    ///
    /// Status = "Sent":
    /// - Chuyển SMS sang state <c>Sent</c>, set <c>SentAt = now</c>.
    /// - Tăng <c>SmsGatewayDevice.SentToday</c> để áp daily limit.
    /// - Publish event <c>SmsDeliveryReportEvent</c> qua Outbox (subscriber như AuthService dùng để log audit OTP).
    /// - Sau 24h, <c>SmsMessageRedactorBackgroundService</c> sẽ xóa cột <c>message</c> để bảo mật.
    ///
    /// Status = "Failed":
    /// - Nếu <c>RetryCount + 1 &lt; MaxRetryCount</c> (default 3): quay về <c>Pending</c>, bump <c>RetryCount</c>, device khác có thể pick lại.
    /// - Ngược lại (đã exhaust retry): chuyển sang <c>Failed</c> final, publish <c>SmsFailedEvent</c> (FinalFailure = true) qua Outbox.
    /// - <c>ErrorMessage</c> được lưu vào DB để debug.
    ///
    /// Idempotency:
    /// - Flutter cứ gọi lại endpoint này nếu mạng chập chờn — chỉ lần đầu (state == Sending) thực sự ghi.
    /// - <c>SmsId</c> field từ body — backend NHẬN từ <c>ReportSmsRequest</c> (không từ route).
    ///
    /// Atomic guarantee:
    /// - Outbox event được publish TRƯỚC <c>SaveChangesAsync()</c> → cùng transaction với business data (no dual-write).
    /// </remarks>
    /// <param name="req">Body chứa <c>SmsId</c> + <c>Status</c> ("Sent"/"Failed") + <c>ErrorMessage</c> (optional khi Failed).</param>
    /// <param name="ct">Token hủy request.</param>
    /// <returns>Status hiện tại của SMS sau khi xử lý (string đại diện <c>SmsStatus</c> enum).</returns>
    /// <response code="200">OK — đã apply report HOẶC ignored (idempotent no-op cho state ngoài Sending).</response>
    /// <response code="400">Status không phải "Sent"/"Failed" → ListErrors có field <c>Status</c>.</response>
    /// <response code="401">Missing/invalid API key.</response>
    /// <response code="403">Device này không phải owner của SMS (đã bị device khác claim sau khi reaper revert).</response>
    /// <response code="404">SMS Id không tồn tại hoặc đã bị soft-delete.</response>
    /// <response code="429">Vượt rate limit 60 req/phút/device.</response>
    [HttpPost("messages/report")]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Report([FromBody] ReportSmsRequest req, CancellationToken ct)
    {
        var resp = await _mediator.Send(new ReportSmsResultCommand
        {
            DeviceId = DeviceId,
            DeviceCode = DeviceCode,
            SmsId = req.SmsId,
            Status = req.Status,
            ErrorMessage = req.ErrorMessage
        }, ct);
        return StatusCode(resp.StatusCode == 0 ? 200 : resp.StatusCode, resp);
    }

    /// <summary>
    /// Heartbeat — Flutter app gọi định kỳ 60s/lần để cập nhật <c>LastSeenAt</c> + <c>LastSeenIp</c>.
    /// </summary>
    /// <remarks>
    /// Server-side dùng <c>LastSeenAt</c> để:
    /// - Admin biết device nào còn online (Grafana dashboard / monitoring alert nếu mất heartbeat &gt; 10 phút).
    /// - Audit IP gửi SMS để forensic (vd device bị compromised).
    ///
    /// Tần suất khuyến nghị:
    /// - 60 giây/lần khi đang active.
    /// - Khi app background hoặc màn hình tắt: vẫn nên giữ heartbeat để backend không mark "offline".
    ///
    /// Lưu ý:
    /// - Không cần body — IP lấy từ <c>HttpContext.Connection.RemoteIpAddress</c>.
    /// - <c>DeviceId</c> lấy từ JWT-like claim của <c>GatewayApiKey</c> scheme.
    /// - Endpoint này có tính vào rate limit 60 req/phút/device — heartbeat 60s/lần dùng ~1/60 budget.
    /// </remarks>
    /// <param name="ct">Token hủy request.</param>
    /// <returns>Trả về string "pong" trong <c>Data</c>.</returns>
    /// <response code="200">OK — heartbeat đã ghi nhận. Body Data = "pong".</response>
    /// <response code="401">Missing/invalid API key.</response>
    /// <response code="404">Device record không tồn tại (đã bị admin xóa cứng — hiếm).</response>
    /// <response code="429">Vượt rate limit 60 req/phút/device.</response>
    [HttpPost("heartbeat")]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Heartbeat(CancellationToken ct)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var resp = await _mediator.Send(new HeartbeatCommand { DeviceId = DeviceId, Ip = ip }, ct);
        return StatusCode(resp.StatusCode == 0 ? 200 : resp.StatusCode, resp);
    }
}
