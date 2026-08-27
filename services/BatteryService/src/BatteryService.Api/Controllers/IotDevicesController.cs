using BatteryService.Api.Authentication;
using BatteryService.Application.CQRS.Command.IotDevice;
using BatteryService.Application.CQRS.Query.IotDevice;
using BatteryService.Application.DTOs;
using BatteryService.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedContracts.Common.Responses;

namespace BatteryService.Api.Controllers;

/// <summary>
/// Nhóm endpoint xử lý vòng đời ESP32 edge device — Sprint IoT-1 (#245).
/// Chính các device tự gọi: provision sau khi flash, heartbeat định kỳ, check OTA, báo cáo progress flash.
/// </summary>
/// <remarks>
/// Đặc thù endpoint:
/// <list type="bullet">
///   <item><description><b>Auth bằng API key per-device</b> (<c>X-Api-Key: iotk_...</c>) — không phải JWT. Khác hoàn toàn các admin endpoint.</description></item>
///   <item><description>Mỗi endpoint declare <see cref="IotApiKeyScopeRequirementAttribute"/> scope cần thiết. Auth handler tự reject 401 nếu scope không khớp.</description></item>
///   <item><description><c>DeviceId</c> + <c>DeviceCode</c> được trích từ claim của API key — client KHÔNG cần (và KHÔNG được) gửi qua body. Field tương ứng trong command có <c>[JsonIgnore][BindNever]</c>.</description></item>
/// </list>
///
/// Lý do dùng ApiKey thay vì JWT:
/// <list type="bullet">
///   <item><description>ESP32 không có khả năng refresh token / xử lý OAuth flow.</description></item>
///   <item><description>API key cố định lưu trong NVS partition của ESP32 sau provision, rotate được qua admin endpoint.</description></item>
///   <item><description>Tách kênh device khỏi kênh user/web giúp rate limit và monitor riêng (gateway có route riêng <c>/api/iot-devices/*</c>).</description></item>
/// </list>
///
/// Lifecycle thông thường:
/// <code>
/// boot → /provision (1 lần)  →  /heartbeat (60s)  →  /sensor-readings/batch (15s)
///                              ↘ /firmware-check (1h) → /firmware-update-log/{id} (sau flash)
/// </code>
/// </remarks>
[ApiController]
[Route("api/iot-devices")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationHandler.SchemeName)]
public class IotDevicesController : ControllerBase
{
    private readonly IMediator _mediator;

    public IotDevicesController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Device báo "tôi đã boot xong" — provision lần đầu hoặc sau khi flash firmware mới.
    /// </summary>
    /// <remarks>
    /// Authentication:
    /// <list type="bullet">
    ///   <item><description>Yêu cầu <c>X-Api-Key: iotk_...</c> per-device, scope <c>DeviceHeartbeat</c>.</description></item>
    ///   <item><description><c>X-Device-Code</c> tùy chọn nhưng <b>khuyến nghị</b> — handler cross-check với <c>DeviceCode</c> đã issue cho key. Mismatch → 403.</description></item>
    /// </list>
    ///
    /// Body request:
    /// <list type="bullet">
    ///   <item><description><c>FirmwareVersion</c>: bắt buộc, version đang chạy (vd <c>"1.0.0"</c>). Hệ thống lưu vào <c>IotDevice.CurrentFirmwareVersion</c>.</description></item>
    ///   <item><description><c>HardwareRevision</c>: tùy chọn. Nếu khác giá trị admin đăng ký, sẽ overwrite — dùng cho trường hợp swap hardware.</description></item>
    ///   <item><description><c>DeviceTimestamp</c>: bắt buộc, UTC timestamp tại thời điểm gọi. Backend so với <c>UtcNow</c> để check clock skew.</description></item>
    /// </list>
    ///
    /// Cách hoạt động:
    /// <list type="bullet">
    ///   <item><description>Auth handler load device từ DB → set <c>DeviceId</c>, <c>DeviceCode</c> vào claim. Controller copy vào command.</description></item>
    ///   <item><description>Validate device tồn tại (404), check <c>DeviceCode</c> claim khớp request (403), check status <c>Disabled/Decommissioned</c> (409).</description></item>
    ///   <item><description>Tính skew giữa <c>DeviceTimestamp</c> và server time. Nếu &gt; 300s (5 phút) → 422 (yêu cầu device sync NTP).</description></item>
    ///   <item><description>Update <c>CurrentFirmwareVersion</c>, <c>HardwareRevision</c> (nếu có), set <c>Status = Active</c>, <c>LastProvisionedAt = LastSeenAt = UtcNow</c>.</description></item>
    ///   <item><description>Trả <see cref="IotDeviceProvisionResultDto"/>: <c>HeartbeatIntervalSeconds</c>, <c>ApiKeyScopes</c>, <c>TargetFirmwareVersion</c> (nếu có) để firmware bootstrap config.</description></item>
    /// </list>
    ///
    /// Lưu ý:
    /// <list type="bullet">
    ///   <item><description><b>Gọi 1 lần sau mỗi lần boot/reset</b> — không phải định kỳ. Heartbeat mới là định kỳ.</description></item>
    ///   <item><description>Nếu skew quá ngưỡng (422), firmware nên sync NTP rồi retry. Đừng raw insert sensor reading trước khi pass provision.</description></item>
    ///   <item><description>Endpoint KHÔNG sinh API key — chỉ admin sinh. Device chỉ "khai báo tôi đây" với key đã có sẵn.</description></item>
    /// </list>
    /// </remarks>
    /// <param name="body">Firmware/hardware info + timestamp.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa <see cref="IotDeviceProvisionResultDto"/>.</returns>
    /// <response code="200">Provision thành công.</response>
    /// <response code="401">Không có / sai API key hoặc thiếu scope <c>DeviceHeartbeat</c>.</response>
    /// <response code="403"><c>X-Device-Code</c> hoặc body <c>DeviceCode</c> không khớp với key.</response>
    /// <response code="404">Device không tồn tại (có thể đã bị admin xóa).</response>
    /// <response code="409">Device <c>Disabled</c> / <c>Decommissioned</c>.</response>
    /// <response code="422">Clock skew &gt; 5 phút — đồng bộ NTP rồi thử lại.</response>
    [HttpPost("provision")]
    [IotApiKeyScopeRequirement(IotApiKeyScopeEnum.DeviceHeartbeat)]
    [ProducesResponseType(typeof(CommonResponse<IotDeviceProvisionResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(CommonResponse<IotDeviceProvisionResultDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<IotDeviceProvisionResultDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(CommonResponse<IotDeviceProvisionResultDto>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(CommonResponse<IotDeviceProvisionResultDto>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Provision([FromBody] ProvisionIotDeviceCommand body, CancellationToken ct)
    {
        if (!TryGetDeviceContext(out var deviceId, out var deviceCode))
            return StatusCode(401, new CommonResponse<object> { IsSuccess = false, StatusCode = 401, Message = "Authentication with a per-device API key is required." });

        body.DeviceId = deviceId;
        body.DeviceCode = deviceCode;
        var result = await _mediator.Send(body, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Heartbeat — telemetry health định kỳ. Backend dùng để phát hiện device offline + ack OTA available.
    /// </summary>
    /// <remarks>
    /// Authentication: yêu cầu scope <c>DeviceHeartbeat</c>.
    ///
    /// Body request:
    /// <list type="bullet">
    ///   <item><description><c>FirmwareVersion</c>: tùy chọn — nếu có, cập nhật <c>IotDevice.CurrentFirmwareVersion</c> (phát hiện flash thành công gián tiếp).</description></item>
    ///   <item><description><c>RssiDbm</c>: tùy chọn, signal WiFi (vd <c>-55</c> mạnh, <c>-85</c> yếu).</description></item>
    ///   <item><description><c>FreeMemoryPercent</c>: tùy chọn [0, 100].</description></item>
    ///   <item><description><c>UptimeSeconds</c>: tùy chọn — phát hiện unexpected reboot khi heartbeat trước có uptime lớn hơn.</description></item>
    ///   <item><description><c>QueuedReadingCount</c>: tùy chọn — số reading device đang queue local do mất mạng. Backend monitor để cảnh báo khi nhiều device cùng queue cao.</description></item>
    ///   <item><description><c>DeviceTimestamp</c>: bắt buộc, UTC. Backend tính skew để cảnh báo clock drift.</description></item>
    /// </list>
    ///
    /// Cách hoạt động:
    /// <list type="bullet">
    ///   <item><description>Insert row vào hypertable <c>iot_device_heartbeats</c> (TimescaleDB, retention 30 ngày).</description></item>
    ///   <item><description>Update <c>IotDevice.LastSeenAt = UtcNow</c>, <c>LastClockSkewSeconds = skew</c>.</description></item>
    ///   <item><description><c>Pending</c> được kích hoạt ngay; <c>Offline</c> cần hai tín hiệu khỏe liên tiếp trong cadence kỳ vọng trước khi về <c>Active</c>, rồi tự resolve incident offline.</description></item>
    ///   <item><description>Trả <see cref="IotHeartbeatAckDto"/>: <c>ServerTime</c>, <c>ClockSkewSeconds</c>, <c>ClockSkewWarning</c> (nếu &gt; 300s), <c>NextHeartbeatInSeconds</c>, <c>FirmwareUpdateAvailable</c>.</description></item>
    /// </list>
    ///
    /// Lưu ý:
    /// <list type="bullet">
    ///   <item><description><b>Không reject heartbeat khi skew quá ngưỡng</b> — chỉ raise <c>ClockSkewWarning</c>. Firmware nên auto-NTP-sync khi nhận warning.</description></item>
    ///   <item><description>Khoảng cách heartbeat khuyến nghị: 60s. Cấu hình ở <c>IotDevice.HeartbeatIntervalSeconds</c> (admin set [10, 3600]). Device nên dùng giá trị từ ack chứ không hard-code.</description></item>
    ///   <item><description>Polling fallback mặc định chạy 120s/lần. Ngưỡng thực tế của từng device là max(<c>Iot:OfflineAfterSeconds</c> mặc định 300s, <c>HeartbeatIntervalSeconds + 30s</c>), nên cadence dài không bị đánh offline giả.</description></item>
    ///   <item><description>MQTT LWT live chuyển Offline ngay khi broker xác nhận socket đã mất; chỉ retained/stale LWT replay mới phải qua <c>Mqtt:LwtOfflineGraceSeconds</c> (mặc định 90s) và freshness check.</description></item>
    ///   <item><description><c>FirmwareUpdateAvailable = true</c> chỉ là hint — firmware vẫn phải gọi <c>/firmware-check</c> để lấy artifact URL + checksum.</description></item>
    /// </list>
    /// </remarks>
    /// <param name="body">Heartbeat telemetry.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa <see cref="IotHeartbeatAckDto"/>.</returns>
    /// <response code="200">Heartbeat ack thành công.</response>
    /// <response code="401">Không có / sai API key hoặc thiếu scope <c>DeviceHeartbeat</c>.</response>
    /// <response code="404">Device không tồn tại.</response>
    /// <response code="409">Device <c>Disabled</c> / <c>Decommissioned</c> hoặc API key đã revoke.</response>
    [HttpPost("heartbeat")]
    [IotApiKeyScopeRequirement(IotApiKeyScopeEnum.DeviceHeartbeat)]
    [ProducesResponseType(typeof(CommonResponse<IotHeartbeatAckDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(CommonResponse<IotHeartbeatAckDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(CommonResponse<IotHeartbeatAckDto>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Heartbeat([FromBody] IotDeviceHeartbeatCommand body, CancellationToken ct)
    {
        if (!TryGetDeviceContext(out var deviceId, out var deviceCode))
            return StatusCode(401, new CommonResponse<object> { IsSuccess = false, StatusCode = 401, Message = "Authentication with a per-device API key is required." });

        body.DeviceId = deviceId;
        body.DeviceCode = deviceCode;
        var result = await _mediator.Send(body, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Device polling check firmware mới (gọi mỗi 1h từ ESP32) — trả {hasUpdate, version, downloadUrl signed, sha256, isRequired, releaseNotes}; tạo UpdateLog Pending để track.
    /// </summary>
    /// <remarks>
    /// Authentication: yêu cầu scope <c>FirmwareCheck</c>.
    ///
    /// Query parameter:
    /// <list type="bullet">
    ///   <item><description><c>currentVersion</c>: bắt buộc, version đang chạy (vd <c>"1.0.0"</c>).</description></item>
    /// </list>
    ///
    /// Cách hoạt động:
    /// <list type="bullet">
    ///   <item><description>Load device + <c>TargetFirmwareRelease</c>.</description></item>
    ///   <item><description>Nếu device không có target, hoặc target <c>!IsPublished</c> / <c>IsArchived</c>, hoặc target version trùng <c>currentVersion</c> → trả <c>UpdateAvailable = false</c>.</description></item>
    ///   <item><description>Ngược lại: tạo 1 <c>IotFirmwareUpdateLog</c> mới <c>Status = Pending</c> (track attempt), trả URL + checksum + <c>UpdateLogId</c> để device theo dõi progress.</description></item>
    /// </list>
    ///
    /// Lưu ý:
    /// <list type="bullet">
    ///   <item><description><b>Mỗi lần gọi với <c>currentVersion</c> khác target sẽ tạo 1 log mới</b> — đừng spam endpoint (khuyến nghị poll mỗi 1 giờ qua firmware <c>main.ino</c>).</description></item>
    ///   <item><description>Sau khi nhận artifact: device download → verify SHA-256 → flash → PUT <c>/firmware-update-log/{UpdateLogId}</c> với <c>Status = Success/Failed</c>.</description></item>
    ///   <item><description>Nếu device không gọi <c>/firmware-update-log</c> sau X giờ, admin có thể xem log <c>Status = Pending</c> tồn đọng và alert (Sprint IoT-2 sẽ có background cleanup).</description></item>
    /// </list>
    /// </remarks>
    /// <param name="currentVersion">Version firmware đang chạy.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa <see cref="IotFirmwareCheckDto"/>.</returns>
    /// <response code="200">Trả kết quả check (có thể update hoặc không).</response>
    /// <response code="401">Không có / sai API key hoặc thiếu scope <c>FirmwareCheck</c>.</response>
    /// <response code="404">Device không tồn tại.</response>
    [HttpGet("firmware-check")]
    [IotApiKeyScopeRequirement(IotApiKeyScopeEnum.FirmwareCheck)]
    [ProducesResponseType(typeof(CommonResponse<IotFirmwareCheckDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(CommonResponse<IotFirmwareCheckDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> FirmwareCheck([FromQuery] string currentVersion, CancellationToken ct)
    {
        if (!TryGetDeviceContext(out var deviceId, out _))
            return StatusCode(401, new CommonResponse<object> { IsSuccess = false, StatusCode = 401, Message = "Authentication with a per-device API key is required." });

        var result = await _mediator.Send(new CheckIotFirmwareUpdateQuery
        {
            DeviceId = deviceId,
            CurrentVersion = currentVersion ?? string.Empty
        }, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Device báo cáo progress OTA flash (Downloading / Installing / Success / Failed / Skipped).
    /// </summary>
    /// <remarks>
    /// Authentication: yêu cầu scope <c>FirmwareCheck</c>.
    ///
    /// Route + body:
    /// <list type="bullet">
    ///   <item><description><c>id</c> (route): Guid của <c>IotFirmwareUpdateLog</c> nhận được từ <c>/firmware-check</c>.</description></item>
    ///   <item><description><c>Status</c>: bắt buộc, enum <see cref="IotFirmwareUpdateStatusEnum"/> — <c>Pending=1, Downloading=2, Installing=3, Success=4, Failed=5, Skipped=6</c>.</description></item>
    ///   <item><description><c>BytesDownloaded</c>: tùy chọn — progress bar UI cho admin xem.</description></item>
    ///   <item><description><c>FailureReason</c>: tùy chọn, ≤ 500 ký tự — mô tả lỗi khi <c>Status = Failed</c> (vd <c>"checksum mismatch"</c>, <c>"insufficient flash space"</c>).</description></item>
    /// </list>
    ///
    /// Cách hoạt động:
    /// <list type="bullet">
    ///   <item><description>Tìm log theo <c>(LogId, DeviceId)</c>; 404 nếu không có hoặc log của device khác.</description></item>
    ///   <item><description>Update <c>Status</c>, <c>BytesDownloaded</c>, <c>FailureReason</c>.</description></item>
    ///   <item><description>Khi <c>Status = Success/Failed/Skipped</c>: set <c>CompletedAt = UtcNow</c>.</description></item>
    ///   <item><description>Khi <c>Success</c>: cập nhật <c>IotDevice.CurrentFirmwareVersion = log.ToVersion</c> (heartbeat tiếp theo sẽ confirm).</description></item>
    /// </list>
    ///
    /// Lưu ý:
    /// <list type="bullet">
    ///   <item><description>Khuyến nghị firmware gửi PUT này <b>nhiều lần</b> trong vòng đời 1 update — <c>Pending → Downloading → Installing → Success</c> — để admin xem real-time.</description></item>
    ///   <item><description>Nếu device reboot giữa chừng và không kịp gửi <c>Success</c>, lần <c>/firmware-check</c> tiếp theo sẽ tạo log mới (không reuse log cũ) — log Pending cũ tồn đọng cho audit.</description></item>
    ///   <item><description>FailureReason không sanitize markdown — tránh truyền raw stacktrace lớn (bị truncate ở 500 ký tự).</description></item>
    /// </list>
    /// </remarks>
    /// <param name="id">Id của <c>IotFirmwareUpdateLog</c>.</param>
    /// <param name="body">Status + progress.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> thông báo update thành công.</returns>
    /// <response code="200">Update log thành công.</response>
    /// <response code="401">Không có / sai API key hoặc thiếu scope <c>FirmwareCheck</c>.</response>
    /// <response code="404">Log không tồn tại hoặc không thuộc device đang gọi.</response>
    [HttpPut("firmware-update-log/{id:guid}")]
    [IotApiKeyScopeRequirement(IotApiKeyScopeEnum.FirmwareCheck)]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateFirmwareLog(Guid id, [FromBody] UpdateIotFirmwareUpdateLogCommand body, CancellationToken ct)
    {
        if (!TryGetDeviceContext(out var deviceId, out _))
            return StatusCode(401, new CommonResponse<object> { IsSuccess = false, StatusCode = 401, Message = "Authentication with a per-device API key is required." });

        body.LogId = id;
        body.DeviceId = deviceId;
        var result = await _mediator.Send(body, ct);
        return StatusCode(result.StatusCode, result);
    }

    private bool TryGetDeviceContext(out Guid deviceId, out string deviceCode)
    {
        deviceId = Guid.Empty;
        deviceCode = string.Empty;
        var idClaim = User.FindFirst(ApiKeyAuthenticationHandler.ClaimDeviceId)?.Value;
        var codeClaim = User.FindFirst(ApiKeyAuthenticationHandler.ClaimDeviceCode)?.Value;
        if (string.IsNullOrEmpty(idClaim) || !Guid.TryParse(idClaim, out var id) || string.IsNullOrEmpty(codeClaim))
            return false;
        deviceId = id;
        deviceCode = codeClaim;
        return true;
    }
}
