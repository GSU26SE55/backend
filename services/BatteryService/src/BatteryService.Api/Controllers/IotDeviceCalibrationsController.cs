using BatteryService.Application.CQRS.Command.IotDevice;
using BatteryService.Application.CQRS.Query.IotDevice;
using BatteryService.Application.DTOs;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedContracts.Common.Requests;
using SharedContracts.Common.Responses;

namespace BatteryService.Api.Controllers;

/// <summary>
/// Sprint IoT-2 #IoT2-32 / #IoT2-33 (S7-BE-01..02) — quản lý <b>IotDeviceCalibration</b> profile.
/// Mỗi calibration entry kẹp 1 channel sensor (voltage/current/temperature/...) với hệ số
/// <c>Scale</c> + <c>Offset</c> để backend apply <c>raw * Scale + Offset</c> trước khi insert reading.
/// </summary>
/// <remarks>
/// Use case chính:
/// <list type="bullet">
///   <item><description>Staff định kỳ recalibrate sensor → POST calibration mới (Scale/Offset đo bằng calibrator chuẩn).</description></item>
///   <item><description>Manager dashboard xem calibration nào sắp hết hạn 30 ngày → ưu tiên schedule technician.</description></item>
///   <item><description>Admin xoá calibration sai (Scale/Offset gây drift sensor reading lớn).</description></item>
/// </list>
///
/// Tích hợp pipeline:
/// <list type="bullet">
///   <item><description><c>BatchIngestSensorReadingsCommand</c> lookup calibration qua Redis cache TTL 5 phút (xem #IoT2-19).</description></item>
///   <item><description>POST/DELETE auto-invalidate Redis cache (xem #IoT2-34) — reading kế tiếp dùng giá trị mới ngay.</description></item>
///   <item><description><c>CalibrationExpiryNotificationBackgroundService</c> scan hằng ngày calibration sắp expire 30d → notify Manager (xem #IoT2-33).</description></item>
/// </list>
///
/// Authentication: JWT (Bearer token). Role tách per-endpoint.
/// </remarks>
[ApiController]
[Route("api/iot-devices")]
[Produces("application/json")]
public class IotDeviceCalibrationsController : ControllerBase
{
    private readonly IMediator _mediator;

    public IotDeviceCalibrationsController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Tra cứu device theo <c>deviceCode</c> (mã in trên thân thiết bị) — trả về <see cref="IotDeviceDto"/> gồm <c>id</c> (GUID).
    /// </summary>
    /// <remarks>
    /// Lý do tồn tại:
    /// <list type="bullet">
    ///   <item><description>Các route calibration (<c>{deviceId:guid}/calibrations</c>) keyed theo GUID <c>Id</c>, nhưng Staff cầm thiết bị chỉ đọc được <c>deviceCode</c> (vd <c>"ESP32-SIM-001"</c>) — không có nguồn GUID nào khác (list admin là <c>[Authorize(Admin)]</c>).</description></item>
    ///   <item><description>Endpoint này là cầu nối <c>deviceCode → device.Id</c> cho Staff/Manager, mở khoá luồng calibration trên mobile.</description></item>
    /// </list>
    ///
    /// Cách hoạt động:
    /// <list type="bullet">
    ///   <item><description><c>deviceCode</c> được chuẩn hoá <c>Trim().ToUpperInvariant()</c> (khớp cách Create lưu) rồi match exact trên unique index <c>idx_iot_devices_device_code</c>.</description></item>
    ///   <item><description>Chỉ trả device <c>!IsDeleted</c> — device đã decommission trả 404.</description></item>
    /// </list>
    /// </remarks>
    /// <param name="deviceCode">Mã device in trên thân thiết bị (case-insensitive).</param>
    /// <param name="ct">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa <see cref="IotDeviceDto"/>.</returns>
    /// <response code="200">Tìm thấy device.</response>
    /// <response code="401">Chưa đăng nhập / token hết hạn.</response>
    /// <response code="403">Không có role Admin/Manager/Staff.</response>
    /// <response code="404">Không có device khớp <c>deviceCode</c> (hoặc đã decommission).</response>
    [HttpGet("by-code/{deviceCode}")]
    [Authorize(Roles = "Admin,Manager,Staff")]
    [ProducesResponseType(typeof(CommonResponse<IotDeviceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<IotDeviceDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByCode(string deviceCode, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetIotDeviceByCodeQuery { DeviceCode = deviceCode }, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Lấy danh sách calibration profiles của 1 device — Admin/Manager/Staff đều xem được.
    /// </summary>
    /// <remarks>
    /// Route + query parameters:
    /// <list type="bullet">
    ///   <item><description><c>deviceId</c> (route, bắt buộc): Guid IoT device.</description></item>
    ///   <item><description><c>channel</c> (query, tùy chọn): filter theo channel (vd <c>"voltage"</c>, <c>"current"</c>, <c>"temperature"</c>). Case-insensitive — backend tự lowercase trước khi compare.</description></item>
    ///   <item><description><c>includeExpired</c> (query, tùy chọn, default <c>false</c>): bao gồm calibration đã <c>ExpiresAt &lt; UtcNow</c>.</description></item>
    /// </list>
    ///
    /// Cách hoạt động:
    /// <list type="bullet">
    ///   <item><description>Filter <c>!IsDeleted &amp;&amp; IotDeviceId == deviceId</c> luôn áp dụng.</description></item>
    ///   <item><description>Sort theo <c>CalibratedAt DESC</c> (mới nhất trên đầu).</description></item>
    ///   <item><description>Trả về flat list <see cref="IotDeviceCalibrationDto"/> — KHÔNG pagination (kỳ vọng &lt; 100 entry/device).</description></item>
    /// </list>
    ///
    /// Lưu ý:
    /// <list type="bullet">
    ///   <item><description>Endpoint <b>không</b> kiểm tra device tồn tại — trả empty list nếu deviceId sai (không 404).</description></item>
    ///   <item><description>Để xem chi tiết device + calibration cùng lúc, gọi <c>GET /api/admin/iot-devices/{id}</c> riêng.</description></item>
    /// </list>
    /// </remarks>
    /// <param name="deviceId">Id IoT device.</param>
    /// <param name="channel">Filter theo channel sensor (voltage/current/temperature). Tùy chọn.</param>
    /// <param name="includeExpired">Bao gồm calibration đã hết hạn. Default false.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa list <see cref="IotDeviceCalibrationDto"/>.</returns>
    /// <response code="200">Trả danh sách calibration (có thể rỗng).</response>
    /// <response code="401">Chưa đăng nhập / token hết hạn.</response>
    /// <response code="403">Không có role Admin/Manager/Staff.</response>
    [HttpGet("{deviceId:guid}/calibrations")]
    [Authorize(Roles = "Admin,Manager,Staff")]
    [ProducesResponseType(typeof(CommonResponse<List<IotDeviceCalibrationDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(Guid deviceId, [FromQuery] string? channel, [FromQuery] bool includeExpired, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetIotDeviceCalibrationsQuery
        {
            IotDeviceId = deviceId,
            Channel = channel,
            IncludeExpired = includeExpired
        }, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Tạo calibration mới cho 1 channel của device — Staff/Admin chạy sau khi đo calibrator chuẩn.
    /// </summary>
    /// <remarks>
    /// Route + body:
    /// <list type="bullet">
    ///   <item><description><c>deviceId</c> (route, bắt buộc): Guid IoT device. Field <c>IotDeviceId</c> trong body bị bỏ qua (<c>[JsonIgnore][BindNever]</c>).</description></item>
    ///   <item><description><c>Channel</c> (body, bắt buộc, ≤ 32 ký tự): channel sensor — <c>"voltage"</c> | <c>"current"</c> | <c>"temperature"</c> | <c>"soc"</c>. Backend tự lowercase.</description></item>
    ///   <item><description><c>BatteryAssetId</c> (body, tùy chọn): nếu device đo nhiều pin (RS485 multi-drop), gắn calibration cho 1 pin cụ thể. Null = áp dụng cho mọi pin device đo.</description></item>
    ///   <item><description><c>Scale</c> (body, bắt buộc, ≠ 0): hệ số nhân raw → engineering unit. Default 1.0 (không scale).</description></item>
    ///   <item><description><c>Offset</c> (body, bắt buộc): hệ số cộng (sau scale). Default 0.0.</description></item>
    ///   <item><description><c>Unit</c> (body, bắt buộc, ≤ 16 ký tự): đơn vị engineering output (<c>"V"</c>, <c>"A"</c>, <c>"°C"</c>).</description></item>
    ///   <item><description><c>CalibratedAt</c> (body, bắt buộc, UTC): ngày calibration lấy mẫu thực tế.</description></item>
    ///   <item><description><c>ExpiresAt</c> (body, tùy chọn, UTC): ngày hết hạn — bắt buộc phải sau <c>CalibratedAt</c>. Khuyến nghị 1 năm.</description></item>
    ///   <item><description><c>Notes</c> (body, tùy chọn, ≤ 500 ký tự): ghi chú technician (serial calibrator, env temp lúc đo, etc.).</description></item>
    /// </list>
    ///
    /// Cách hoạt động:
    /// <list type="number">
    ///   <item><description>Field validation 400 (Channel/Unit required, Scale ≠ 0, ExpiresAt &gt; CalibratedAt).</description></item>
    ///   <item><description>Tìm device — 404 nếu không tồn tại hoặc đã <c>IsDeleted</c>.</description></item>
    ///   <item><description>Insert <c>iot_device_calibrations</c> + Sprint IoT-2 #IoT2-34: invalidate Redis cache key <c>iot:calibration:{deviceId}</c>.</description></item>
    ///   <item><description>Reading kế tiếp từ device này sẽ MISS cache → re-fetch DB → dùng calibration mới.</description></item>
    /// </list>
    ///
    /// Lưu ý:
    /// <list type="bullet">
    ///   <item><description><b>Không deactivate calibration cũ</b> — handler ingest lookup theo <c>(channel, batteryAssetId)</c>; entry mới nhất theo <c>CalibratedAt</c> được dùng. Nếu muốn force, xoá entry cũ qua DELETE.</description></item>
    ///   <item><description>Calibration scope device-level (BatteryAssetId = null) ưu tiên thấp hơn asset-level. Asset-level match được pick trước.</description></item>
    ///   <item><description>Multiple calibration cùng channel + cùng BatteryAssetId được phép — handler chỉ dùng row mới nhất chưa expire.</description></item>
    /// </list>
    /// </remarks>
    /// <param name="deviceId">Id IoT device từ route.</param>
    /// <param name="cmd">Calibration metadata.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa <see cref="IotDeviceCalibrationDto"/> vừa tạo.</returns>
    /// <response code="201">Tạo thành công + cache đã invalidate.</response>
    /// <response code="400">Field validation lỗi (xem <c>ListErrors</c> — Field + Detail).</response>
    /// <response code="401">Chưa đăng nhập / token hết hạn.</response>
    /// <response code="403">Không có role Admin/Staff.</response>
    /// <response code="404">Device không tồn tại hoặc đã decommissioned.</response>
    [HttpPost("{deviceId:guid}/calibrations")]
    [Authorize(Roles = "Admin,Staff")]
    [ProducesResponseType(typeof(CommonResponse<IotDeviceCalibrationDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(CommonResponse<IotDeviceCalibrationDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<IotDeviceCalibrationDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create(Guid deviceId, [FromBody] CreateIotDeviceCalibrationCommand cmd, CancellationToken ct)
    {
        cmd.IotDeviceId = deviceId;
        var result = await _mediator.Send(cmd, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Xoá mềm (soft-delete) 1 calibration entry — dùng khi đo sai hoặc thay sensor mới.
    /// </summary>
    /// <remarks>
    /// Route parameters:
    /// <list type="bullet">
    ///   <item><description><c>deviceId</c> (route, bắt buộc): Guid IoT device.</description></item>
    ///   <item><description><c>calibrationId</c> (route, bắt buộc): Guid calibration entry. Phải thuộc <c>deviceId</c> trên (cross-check ở handler).</description></item>
    /// </list>
    ///
    /// Cách hoạt động:
    /// <list type="number">
    ///   <item><description>Tìm entry theo <c>(calibrationId, deviceId)</c> + <c>!IsDeleted</c>; 404 nếu không khớp (sai ID hoặc đã xoá).</description></item>
    ///   <item><description>Gọi <c>DeleteAsync</c> → <c>AuditableEntityInterceptor</c> set <c>IsDeleted=true</c>, <c>DeletedAt=UtcNow</c> (không xoá vật lý).</description></item>
    ///   <item><description>Sprint IoT-2 #IoT2-34: invalidate Redis cache <c>iot:calibration:{deviceId}</c> ngay sau khi commit.</description></item>
    ///   <item><description>Reading kế tiếp re-fetch DB — entry bị xoá không còn được apply.</description></item>
    /// </list>
    ///
    /// Lưu ý:
    /// <list type="bullet">
    ///   <item><description>Xoá calibration <b>không ảnh hưởng</b> các sensor reading đã insert trước đó — chỉ apply cho reading mới.</description></item>
    ///   <item><description>Nếu xoá entry duy nhất của channel → reading kế tiếp dùng <c>Scale=1, Offset=0</c> (raw value).</description></item>
    ///   <item><description>Audit trail: <c>iot_device_calibrations</c> giữ row với <c>DeletedAt</c> cho compliance — query include deleted nếu cần forensic.</description></item>
    /// </list>
    /// </remarks>
    /// <param name="deviceId">Id IoT device.</param>
    /// <param name="calibrationId">Id calibration entry cần xoá.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> với <c>Message</c> "Đã xoá calibration." khi thành công.</returns>
    /// <response code="200">Xoá thành công + cache đã invalidate.</response>
    /// <response code="401">Chưa đăng nhập / token hết hạn.</response>
    /// <response code="403">Không có role Admin/Staff.</response>
    /// <response code="404">Calibration không tồn tại, đã xoá, hoặc không thuộc <c>deviceId</c>.</response>
    [HttpDelete("{deviceId:guid}/calibrations/{calibrationId:guid}")]
    [Authorize(Roles = "Admin,Staff")]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid deviceId, Guid calibrationId, CancellationToken ct)
    {
        var result = await _mediator.Send(new DeleteIotDeviceCalibrationCommand
        {
            IotDeviceId = deviceId,
            CalibrationId = calibrationId
        }, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Sprint IoT-2 #IoT2-32 — list calibration sắp hết hạn trong N ngày tới (Manager dashboard).
    /// </summary>
    /// <remarks>
    /// Query parameter:
    /// <list type="bullet">
    ///   <item><description><c>within</c> (query, tùy chọn, default <c>30</c>): khoảng thời gian (ngày). Clamp <c>[1..365]</c> — value ngoài range tự cap.</description></item>
    /// </list>
    ///
    /// Cách hoạt động:
    /// <list type="bullet">
    ///   <item><description>Filter: <c>!IsDeleted &amp;&amp; ExpiresAt != null &amp;&amp; ExpiresAt &gt; UtcNow &amp;&amp; ExpiresAt &lt;= UtcNow + within ngày</c>.</description></item>
    ///   <item><description>Sort <c>ExpiresAt ASC</c> (sắp expire nhất lên đầu).</description></item>
    ///   <item><description>Trả flat list cross-device — không pagination (kỳ vọng &lt; 200 entry).</description></item>
    /// </list>
    ///
    /// Use case:
    /// <list type="bullet">
    ///   <item><description>Manager dashboard: hiển thị calibration cần lên lịch recalibrate trong tháng tới.</description></item>
    ///   <item><description>Bổ trợ với <c>CalibrationExpiryNotificationBackgroundService</c> — service tự alert; endpoint cho UI chủ động query.</description></item>
    /// </list>
    ///
    /// Lưu ý:
    /// <list type="bullet">
    ///   <item><description>Calibration đã <b>expired</b> (ExpiresAt &lt;= UtcNow) <b>KHÔNG</b> hiển thị — chỉ list những cái sắp hết. Để xem expired, query <c>GET /api/iot-devices/{id}/calibrations?includeExpired=true</c>.</description></item>
    ///   <item><description>Calibration không set <c>ExpiresAt</c> (vĩnh viễn) cũng KHÔNG xuất hiện ở đây.</description></item>
    ///   <item><description>Service background <c>CalibrationExpiryNotificationBackgroundService</c> tự tạo Alert (dedup 7 ngày) khi entry vào range — Manager có thể nhận push notification.</description></item>
    /// </list>
    /// </remarks>
    /// <param name="withinDays">Khoảng ngày (default 30, clamp [1..365]).</param>
    /// <param name="ct">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa list <see cref="IotDeviceCalibrationDto"/> sort ASC theo ExpiresAt.</returns>
    /// <response code="200">Trả danh sách (có thể rỗng nếu không có calibration sắp hết hạn).</response>
    /// <response code="401">Chưa đăng nhập / token hết hạn.</response>
    /// <response code="403">Không có role Admin/Manager/Staff.</response>
    [HttpGet("calibrations-expiring")]
    // IOT3-59 — mở cho Staff. Chính Staff là người CẦM MÁY đi hiệu chuẩn (POST calibration đã cho
    // Staff từ trước); bắt họ hỏi Manager mới biết cái nào sắp hết hạn là chặn đúng người làm việc.
    [Authorize(Roles = "Admin,Manager,Staff")]
    [ProducesResponseType(typeof(CommonResponse<List<IotDeviceCalibrationDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Expiring([FromQuery(Name = "within")] int? withinDays, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetIotCalibrationsExpiringQuery
        {
            WithinDays = withinDays ?? 30
        }, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// IOT3-57 — danh sách IoT device (phân trang, lọc, tìm kiếm) cho <b>Admin, Manager, Staff</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Trước sprint này, đường DUY NHẤT để liệt kê thiết bị là <c>GET /api/admin/iot-devices</c> —
    /// <c>[Authorize(Roles = "Admin")]</c>. Staff cầm thiết bị ngoài hiện trường chỉ đọc được
    /// <c>deviceCode</c> in trên thân máy, không có cách nào biết thiết bị đó còn sống hay không,
    /// firmware nào, lần cuối thấy khi nào.
    /// </para>
    /// <para>
    /// ⚠️ <b>KHÔNG lọc theo site của người gọi</b> (quyết định #7 của sprint). Staff xem được mọi
    /// thiết bị là CỐ Ý, thống nhất với phạm vi tài sản đã chốt ở <c>overall.md §34.10.6</c> — một
    /// Staff có thể được điều sang site khác giữa ca, và chặn theo site sẽ làm họ mất khả năng tra
    /// cứu đúng lúc cần nhất.
    /// </para>
    /// <para>
    /// Trả <see cref="IotDeviceDto"/> thường: <b>không</b> có <c>apiKey</c>, <b>không</b> có
    /// <c>mqttPassword</c>. Hai thứ đó chỉ ở đường admin.
    /// </para>
    /// <para>
    /// ⚠️ Action này nằm trong <c>IotDeviceCalibrationsController</c> vì controller đó đã chiếm
    /// <c>[Route("api/iot-devices")]</c>. Tạo controller mới cùng route sẽ ra
    /// <c>AmbiguousMatchException</c> lúc chạy — build vẫn xanh.
    /// </para>
    /// </remarks>
    /// <param name="query">Tham số phân trang + lọc (siteId, status, keyword, sortBy, sortDir).</param>
    /// <param name="ct">Token huỷ request.</param>
    /// <response code="200">Trang kết quả (có thể rỗng).</response>
    /// <response code="401">Chưa đăng nhập / token hết hạn.</response>
    /// <response code="403">Không có role Admin/Manager/Staff.</response>
    [HttpGet]
    [Authorize(Roles = "Admin,Manager,Staff")]
    [ProducesResponseType(typeof(CommonResponse<PaginationResponse<IotDeviceDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List([FromQuery] GetIotDevicesQuery query, CancellationToken ct)
    {
        var result = await _mediator.Send(query, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// IOT3-58 — lịch sử heartbeat của một thiết bị, phân trang theo <b>con trỏ</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Endpoint này từng được XML doc của <c>AdminIotDevicesController</c> nhắc tới như thể đã có
    /// ("dùng <c>GET .../{id}/heartbeats</c> (Sprint IoT-2)") trong khi chưa hề được viết. Bảng
    /// <c>iot_device_heartbeats</c> đã được ghi từ IoT-1 — chỉ thiếu đường đọc ra.
    /// </para>
    /// <para>
    /// <b>Con trỏ, không offset.</b> Đây là hypertable TimescaleDB, mỗi thiết bị một bản ghi mỗi
    /// 60 giây; <c>OFFSET</c> buộc quét lại từ đầu mỗi trang. Truyền <c>nextCursor</c> của trang
    /// trước vào <c>cursor</c> để lấy trang kế. <c>totalCount</c> luôn <c>null</c> — FE dùng
    /// <c>hasMore</c>.
    /// </para>
    /// <para>Kết quả sắp xếp MỚI TRƯỚC (<c>time</c> giảm dần).</para>
    /// </remarks>
    /// <param name="deviceId">Id thiết bị (GUID). Staff lấy được qua <c>GET by-code/{deviceCode}</c>.</param>
    /// <param name="query">from · to · limit (1–1000, mặc định 100) · cursor.</param>
    /// <param name="ct">Token huỷ request.</param>
    /// <response code="200">Trang kết quả.</response>
    /// <response code="400">Limit ngoài [1, 1000].</response>
    /// <response code="401">Chưa đăng nhập / token hết hạn.</response>
    /// <response code="403">Không có role Admin/Manager/Staff.</response>
    /// <response code="404">Không tìm thấy thiết bị.</response>
    /// <response code="422">Khoảng thời gian ngược (to &lt; from).</response>
    [HttpGet("{deviceId:guid}/heartbeats")]
    [Authorize(Roles = "Admin,Manager,Staff")]
    [ProducesResponseType(typeof(CommonResponse<IotDeviceHeartbeatListDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Heartbeats(
        Guid deviceId, [FromQuery] GetIotDeviceHeartbeatsQuery query, CancellationToken ct)
    {
        query.DeviceId = deviceId;
        var result = await _mediator.Send(query, ct);
        return StatusCode(result.StatusCode, result);
    }
}
