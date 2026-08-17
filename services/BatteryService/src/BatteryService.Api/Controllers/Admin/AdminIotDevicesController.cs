using System.Security.Claims;
using System.Text.Json;
using BatteryService.Application.CQRS.Command.IotDevice;
using BatteryService.Application.CQRS.Query.IotDevice;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Services;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.Infrastructure.Mqtt;   // IOT3-14: MqttTopicMap — dựng topic đúng dạng đã publish
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Api.Controllers.Admin;

/// <summary>
/// Module admin quản lý <b>IotDevice</b> — Sprint IoT-1 (#244).
/// Bao gồm: tạo device + sinh API key, cập nhật, xóa (decommission), rotate/revoke key.
/// Toàn bộ endpoint chỉ dành cho Admin.
/// </summary>
/// <remarks>
/// Quan hệ với các module khác:
/// <list type="bullet">
///   <item><description><b>Device endpoints</b> (<c>POST /api/iot-devices/provision</c>, <c>heartbeat</c>, ...): chính các ESP32 gọi với <c>X-Api-Key</c> per-device được sinh ở đây.</description></item>
///   <item><description><b>Sensor ingest</b> (<c>POST /api/sensor-readings/batch</c>): dùng cùng API key per-device + header <c>X-Device-Code</c>; ApiKey scope phải có <c>SensorIngest</c>.</description></item>
///   <item><description><b>Offline detection</b>: <c>IotDeviceOfflineDetectionBackgroundService</c> chạy 60s/lần, mark device Offline khi <c>LastSeenAt &lt; now - 5 phút</c>.</description></item>
/// </list>
/// </remarks>
// Role is deliberately NOT declared here: ASP.NET Core combines class-level and method-level
// [Authorize(Roles=...)] with AND, not override — a class-level "Admin" would silently block the
// "Admin,Staff" actions below from ever letting Staff through. Every action instead declares its
// own [Authorize(Roles=...)].
[ApiController]
[Route("api/admin/iot-devices")]
[Produces("application/json")]
[ApiExplorerSettings(GroupName = "admin")]
[Authorize]
public class AdminIotDevicesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IMqttBridgePublisher _mqttPublisher;
    private readonly IBatteryUnitOfWork _unitOfWork;

    public AdminIotDevicesController(IMediator mediator, IMqttBridgePublisher mqttPublisher, IBatteryUnitOfWork unitOfWork)
    {
        _mediator = mediator;
        _mqttPublisher = mqttPublisher;
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// Liệt kê IoT device theo filter (status/siteId/keyword) + pagination — dùng cho admin fleet management dashboard, sort theo CreatedAt DESC mặc định.
    /// </summary>
    /// <remarks>
    /// Query parameters:
    /// <list type="bullet">
    ///   <item><description><c>SiteId</c>: tùy chọn, filter theo Site.</description></item>
    ///   <item><description><c>Status</c>: tùy chọn, enum <c>Pending=1 / Active=2 / Offline=3 / Disabled=4 / Decommissioned=5</c>.</description></item>
    ///   <item><description><c>Keyword</c>: tùy chọn, tìm trong <c>DeviceCode</c> + <c>DisplayName</c> (case-insensitive).</description></item>
    ///   <item><description><c>Page</c>: mặc định 1, &gt; 0.</description></item>
    ///   <item><description><c>PageSize</c>: mặc định 20, clamp [1, 100].</description></item>
    ///   <item><description><c>IsDescending</c>: mặc định true (mới nhất trước theo <c>CreatedAt</c>).</description></item>
    /// </list>
    ///
    /// Cách hoạt động:
    /// <list type="bullet">
    ///   <item><description>Filter <c>!IsDeleted</c> luôn áp dụng (decommissioned device đã soft-delete sẽ KHÔNG xuất hiện).</description></item>
    ///   <item><description>Include <c>Site</c> + <c>TargetFirmwareRelease</c> để map ra <c>SiteName</c> và <c>TargetFirmwareVersion</c> trong DTO.</description></item>
    ///   <item><description>Trả <see cref="PaginationResponse{T}"/> chuẩn có <c>TotalItems</c>, <c>HasNextPage</c>, ...</description></item>
    /// </list>
    ///
    /// Lưu ý:
    /// <list type="bullet">
    ///   <item><description><b>Full API key không xuất hiện trong list</b> — chỉ có <c>ApiKeyLastFour</c>. Xem full plaintext key qua <c>GET /api/admin/iot-devices/{id}</c> (field <c>apiKey</c>).</description></item>
    ///   <item><description><c>ApiKeyLastFour</c> hiển thị 4 ký tự cuối để Admin nhận diện key đang dùng mà không lộ key gốc.</description></item>
    /// </list>
    /// </remarks>
    /// <param name="query">Filter + pagination.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa <see cref="PaginationResponse{T}"/> các <see cref="IotDeviceDto"/>.</returns>
    /// <response code="200">Trả danh sách.</response>
    /// <response code="401">Chưa đăng nhập / token hết hạn.</response>
    /// <response code="403">Không có role Admin.</response>
    [HttpGet]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(CommonResponse<PaginationResponse<IotDeviceDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List([FromQuery] GetIotDevicesQuery query, CancellationToken ct)
    {
        var result = await _mediator.Send(query, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Lấy chi tiết 1 IoT device theo Id — bao gồm Site/TargetFirmwareRelease + LastSeenAt + heartbeat stats, kèm <b>full plaintext <c>apiKey</c></b> để Admin xem lại và flash vào ESP32.
    /// </summary>
    /// <remarks>
    /// Route parameter:
    /// <list type="bullet">
    ///   <item><description><c>id</c>: bắt buộc, Guid của device. <b>Không</b> bind từ body/query — chỉ route.</description></item>
    /// </list>
    ///
    /// Cách hoạt động:
    /// <list type="bullet">
    ///   <item><description>Include <c>Site</c> + <c>TargetFirmwareRelease</c> để DTO có <c>SiteName</c>, <c>TargetFirmwareVersion</c>.</description></item>
    ///   <item><description>404 nếu Id không khớp hoặc device đã soft-delete.</description></item>
    ///   <item><description>Trả <see cref="IotDeviceDetailDto"/> — giống <see cref="IotDeviceDto"/> nhưng có thêm field <c>apiKey</c> (plaintext đầy đủ).</description></item>
    /// </list>
    ///
    /// Về <c>apiKey</c> (khác các endpoint khác):
    /// <list type="bullet">
    ///   <item><description>Đây là endpoint <b>duy nhất</b> trả full plaintext key ngoài lúc create/rotate — endpoint <c>list</c> chỉ trả <c>apiKeyLastFour</c>.</description></item>
    ///   <item><description><c>apiKey = null</c> nếu device được tạo <b>trước</b> khi bật lưu plaintext (không backfill được vì DB cũ chỉ giữ hash). Gọi <c>POST /{id}/rotate-key</c> để sinh key mới + lưu plaintext.</description></item>
    ///   <item><description>Role <c>Admin</c> hoặc <c>Staff</c> gọi được (<c>[Authorize(Roles = "Admin,Staff")]</c> trên chính action này).</description></item>
    /// </list>
    ///
    /// Lưu ý:
    /// <list type="bullet">
    ///   <item><description>Endpoint <b>không</b> trả heartbeat history — dùng <c>GET /api/admin/iot-devices/{id}/heartbeats</c> (Sprint IoT-2) khi cần xem chuỗi heartbeat.</description></item>
    ///   <item><description>Để xem alert <c>DeviceOffline</c> gắn với device, query <c>/api/alerts?anomalyType=7&amp;assetId=...</c>.</description></item>
    /// </list>
    /// </remarks>
    /// <param name="id">Id IoT device.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa <see cref="IotDeviceDetailDto"/> (kèm full <c>apiKey</c>).</returns>
    /// <response code="200">Trả device kèm full plaintext <c>apiKey</c> (hoặc <c>null</c> nếu device cũ chưa có).</response>
    /// <response code="401">Chưa đăng nhập / token hết hạn.</response>
    /// <response code="403">Không có role Admin.</response>
    /// <response code="404">Không tìm thấy device.</response>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Admin,Staff")]
    [ProducesResponseType(typeof(CommonResponse<IotDeviceDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<IotDeviceDetailDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetIotDeviceByIdQuery { Id = id }, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Tạo IoT device mới + sinh API key per-device + MQTT credential. Response trả raw API key
    /// và raw MQTT password. <b>MQTT password chỉ trả 1 lần</b> (DB chỉ giữ hash);
    /// <b>API key thì đọc lại được</b> qua <c>GET /{id}</c> (GH-724 — có chủ ý).
    /// </summary>
    /// <remarks>
    /// Body request:
    /// <list type="bullet">
    ///   <item><description><c>DeviceCode</c>: bắt buộc, 3-64 ký tự, regex <c>^[A-Z0-9-]+$</c>. Hệ thống tự <c>Trim().ToUpperInvariant()</c> trước khi check trùng.</description></item>
    ///   <item><description><c>DisplayName</c>: bắt buộc, ≤ 200 ký tự.</description></item>
    ///   <item><description><c>SiteId</c>: bắt buộc, phải tồn tại + chưa xóa.</description></item>
    ///   <item><description><c>HardwareRevision</c>: tùy chọn, ≤ 64 ký tự (vd <c>"v1.0-S3-MAX485"</c>) — dùng để OTA pipeline khớp firmware.</description></item>
    ///   <item><description><c>ApiKeyScopes</c>: bitmask <see cref="BatteryService.Domain.Enums.IotApiKeyScopeEnum"/>. Default <c>EdgeDeviceDefault = SensorIngest | DeviceHeartbeat | EnvironmentalIngest | FirmwareCheck = 15</c>.</description></item>
    ///   <item><description><c>HeartbeatIntervalSeconds</c>: [10, 3600]. Default 60.</description></item>
    ///   <item><description><c>Notes</c>: tùy chọn, ≤ 1000 ký tự.</description></item>
    /// </list>
    ///
    /// Cách hoạt động:
    /// <list type="bullet">
    ///   <item><description>Validate field (400) → check trùng <c>DeviceCode</c> (409) → check Site tồn tại (404).</description></item>
    ///   <item><description>Sinh raw key 256-bit prefix <c>iotk_</c> (~47 ký tự), hash SHA-256 lưu DB, lưu 4 ký tự cuối làm UI hint.</description></item>
    ///   <item><description>Device <c>Status = Pending</c> ban đầu — phải gọi <c>POST /api/iot-devices/provision</c> mới chuyển <c>Active</c>.</description></item>
    ///   <item><description>Trả <see cref="IotDeviceCreatedDto"/> với <c>RawApiKey</c> nguyên bản — copy ngay vào secrets vault / NVS partition của ESP32.</description></item>
    /// </list>
    ///
    /// Lưu ý:
    /// <list type="bullet">
    ///   <item><description>GH-724 — <b>RawApiKey KHÔNG phải "chỉ 1 lần"</b>: hệ thống giữ cả hash lẫn plaintext, Admin đọc lại được qua <c>GET /{id}</c>. Mất key vẫn có thể rotate. Ngược lại, <b>raw MQTT password đúng là chỉ 1 lần</b> — DB chỉ giữ <c>MqttPasswordHash</c>.</description></item>
    ///   <item><description>Endpoint không gửi <c>RawApiKey</c> qua log/audit/event — chỉ trong response body. Đảm bảo client không log nó.</description></item>
    ///   <item><description>Sau khi tạo, gắn calibration profile (<c>iot_device_calibrations</c>) cho từng channel nếu BMS có offset/scale riêng — endpoint riêng (Sprint IoT-2).</description></item>
    /// </list>
    /// </remarks>
    /// <param name="cmd">Thông tin device cần tạo.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa <see cref="IotDeviceCreatedDto"/> bao gồm raw API key.</returns>
    /// <response code="201">Tạo thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ (xem <c>ListErrors</c>).</response>
    /// <response code="401">Chưa đăng nhập / token hết hạn.</response>
    /// <response code="403">Không có role Admin.</response>
    /// <response code="404">Site không tồn tại.</response>
    /// <response code="409"><c>DeviceCode</c> đã tồn tại.</response>
    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(CommonResponse<IotDeviceCreatedDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(CommonResponse<IotDeviceCreatedDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<IotDeviceCreatedDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(CommonResponse<IotDeviceCreatedDto>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateIotDeviceCommand cmd, CancellationToken ct)
    {
        var result = await _mediator.Send(cmd, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Cập nhật metadata + status + scopes + target firmware của 1 device.
    /// </summary>
    /// <remarks>
    /// Route + body:
    /// <list type="bullet">
    ///   <item><description><c>id</c> (route, bắt buộc): Guid device. Field <c>Id</c> trong body bị bỏ qua do <c>[JsonIgnore][BindNever]</c>.</description></item>
    ///   <item><description><c>DisplayName</c>: bắt buộc, ≤ 200 ký tự.</description></item>
    ///   <item><description><c>SiteId</c>: bắt buộc — đổi site khi chuyển device sang vị trí khác. Site phải tồn tại + chưa xóa.</description></item>
    ///   <item><description><c>HardwareRevision</c>: tùy chọn, ≤ 64 ký tự.</description></item>
    ///   <item><description><c>Status</c>: enum <see cref="BatteryService.Domain.Enums.IotDeviceStatusEnum"/>. Cho phép manual set <c>Disabled</c> (block heartbeat) hoặc <c>Decommissioned</c>.</description></item>
    ///   <item><description><c>ApiKeyScopes</c>: bitmask. Set lại scopes (vd thu hồi <c>FirmwareCheck</c> khi không cần OTA).</description></item>
    ///   <item><description><c>HeartbeatIntervalSeconds</c>: [10, 3600].</description></item>
    ///   <item><description><c>TargetFirmwareReleaseId</c>: tùy chọn — set target để OTA. Release phải tồn tại + <c>IsPublished = true</c> + <c>!IsArchived</c>.</description></item>
    ///   <item><description><c>Notes</c>: tùy chọn, ≤ 1000 ký tự.</description></item>
    /// </list>
    ///
    /// Cách hoạt động:
    /// <list type="bullet">
    ///   <item><description>Tìm device + include <c>Site</c>, <c>TargetFirmwareRelease</c>; 404 nếu không có.</description></item>
    ///   <item><description>Nếu đổi <c>SiteId</c> → query Site mới (404 nếu không có).</description></item>
    ///   <item><description>Nếu set <c>TargetFirmwareReleaseId</c>: tách 2 case — không tồn tại = 404; tồn tại nhưng <c>!IsPublished</c> hoặc <c>IsArchived</c> = 409 (state conflict).</description></item>
    ///   <item><description>Update tất cả field → SaveChanges. Heartbeat tiếp theo sẽ ack <c>FirmwareUpdateAvailable = true</c> nếu target khác current.</description></item>
    /// </list>
    ///
    /// Lưu ý:
    /// <list type="bullet">
    ///   <item><description>Endpoint này <b>không</b> đổi API key — dùng <c>/rotate-key</c>.</description></item>
    ///   <item><description>Đặt <c>Status = Disabled</c> không revoke key — vẫn cần gọi <c>/revoke-key</c> nếu muốn block ingest hoàn toàn (handler heartbeat trả 409 khi Status = Disabled).</description></item>
    ///   <item><description><c>Status = Decommissioned</c> nên đi cùng <c>DELETE</c> hoặc <c>/revoke-key</c> để cleanly retire device.</description></item>
    /// </list>
    /// </remarks>
    /// <param name="id">Id device.</param>
    /// <param name="cmd">Thông tin update.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa <see cref="IotDeviceDto"/> sau update.</returns>
    /// <response code="200">Update thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ.</response>
    /// <response code="401">Chưa đăng nhập / token hết hạn.</response>
    /// <response code="403">Không có role Admin.</response>
    /// <response code="404">Device / Site / Firmware release không tồn tại.</response>
    /// <response code="409">Target firmware đang ở trạng thái <c>!IsPublished</c> hoặc <c>IsArchived</c>.</response>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(CommonResponse<IotDeviceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<IotDeviceDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<IotDeviceDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(CommonResponse<IotDeviceDto>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateIotDeviceCommand cmd, CancellationToken ct)
    {
        cmd.Id = id;
        var result = await _mediator.Send(cmd, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Xoá mềm (decommission) 1 device — set Status=Decommissioned + revoke API key. Heartbeat tiếp theo bị reject 401; sensor readings cũ giữ cho forensic.
    /// </summary>
    /// <remarks>
    /// Cách hoạt động:
    /// <list type="bullet">
    ///   <item><description>Tìm device; 404 nếu không có.</description></item>
    ///   <item><description>Set <c>Status = Decommissioned</c>, <c>ApiKeyRevokedAt = UtcNow</c>, gọi <c>DeleteAsync</c> → <c>AuditableEntityInterceptor</c> tự set <c>IsDeleted = true</c>, <c>DeletedAt = UtcNow</c>.</description></item>
    ///   <item><description>Sau khi delete: device không xuất hiện trong list/get-by-id, heartbeat tiếp theo trả 409, ingest trả 403 (API key không match active row).</description></item>
    /// </list>
    ///
    /// Lưu ý:
    /// <list type="bullet">
    ///   <item><description><c>iot_device_heartbeats</c> history KHÔNG bị xóa (hypertable không có <c>IsDeleted</c>) — vẫn truy vấn được cho audit/forensic.</description></item>
    ///   <item><description><c>iot_firmware_update_logs</c> giữ nguyên — track lịch sử OTA của device đã decommission.</description></item>
    ///   <item><description><c>iot_device_calibrations</c> bị cascade soft-delete theo FK.</description></item>
    ///   <item><description>Alert <c>DeviceOffline</c> gắn với device không bị tự động resolve — nếu muốn cleanup, dùng <c>/api/admin/alerts/{id}</c> riêng.</description></item>
    /// </list>
    /// </remarks>
    /// <param name="id">Id device cần decommission.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> thông báo thành công.</returns>
    /// <response code="200">Decommission thành công.</response>
    /// <response code="401">Chưa đăng nhập / token hết hạn.</response>
    /// <response code="403">Không có role Admin.</response>
    /// <response code="404">Không tìm thấy device.</response>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new DeleteIotDeviceCommand { Id = id }, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Rotate API key per-device — sinh key mới, vô hiệu hoá key cũ.
    /// </summary>
    /// <remarks>
    /// Endpoint dành riêng cho rotate vì là hành động nghiệp vụ đặc biệt:
    /// <list type="bullet">
    ///   <item><description>Sinh raw key 256-bit mới (prefix <c>iotk_</c>).</description></item>
    ///   <item><description>Replace <c>ApiKeyHash</c> + <c>ApiKeyLastFour</c> + <c>ApiKeyPlaintext</c> trong DB (GH-724 — plaintext được lưu lại có chủ ý).</description></item>
    ///   <item><description>Reset <c>ApiKeyIssuedAt = UtcNow</c>, set <c>ApiKeyRevokedAt = null</c> (cho phép device dùng lại sau khi từng revoke).</description></item>
    ///   <item><description>Trả <see cref="IotDeviceCreatedDto"/> với <c>RawApiKey</c> mới (đọc lại được sau đó qua <c>GET /{id}</c> — GH-724).</description></item>
    /// </list>
    ///
    /// Use case:
    /// <list type="bullet">
    ///   <item><description>Key bị lộ → rotate gấp + flash key mới vào ESP32 qua serial / OTA config.</description></item>
    ///   <item><description>Compliance policy (vd 90 ngày rotate).</description></item>
    ///   <item><description>Sau khi <c>/revoke-key</c> giờ muốn reactivate device.</description></item>
    /// </list>
    ///
    /// Lưu ý:
    /// <list type="bullet">
    ///   <item><description><b>Device đang chạy với key cũ sẽ ngay lập tức bị reject 401 ở ingest/heartbeat</b> — phải flash key mới trước hoặc song song lên ESP32.</description></item>
    ///   <item><description>Endpoint KHÔNG đổi <c>Status</c> — nếu device đang <c>Disabled</c> phải <c>PUT</c> riêng để chuyển <c>Active</c>.</description></item>
    ///   <item><description>Audit: rotate được log qua <c>AuditableEntityInterceptor</c> (UpdatedAt). Sprint IoT-2 sẽ thêm <c>iot_device_key_history</c> track full rotation chain.</description></item>
    /// </list>
    /// </remarks>
    /// <param name="id">Id device.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa <see cref="IotDeviceCreatedDto"/> bao gồm raw API key mới.</returns>
    /// <response code="200">Rotate thành công.</response>
    /// <response code="401">Chưa đăng nhập / token hết hạn.</response>
    /// <response code="403">Không có role Admin.</response>
    /// <response code="404">Không tìm thấy device.</response>
    [HttpPost("{id:guid}/rotate-key")]
    [Authorize(Roles = "Admin,Staff")]
    [ProducesResponseType(typeof(CommonResponse<IotDeviceCreatedDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<IotDeviceCreatedDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RotateKey(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new RotateIotDeviceApiKeyCommand { Id = id }, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// IOT3-32 — xoay RIÊNG thông tin đăng nhập MQTT. Thiết bị tự lấy lại, KHÔNG cần ra hiện trường.
    /// </summary>
    /// <remarks>
    /// Khác <c>/rotate-key</c> ở điểm quyết định:
    /// <list type="bullet">
    ///   <item><description>
    ///     <c>/rotate-key</c> đổi cả <c>apiKey</c> ⇒ thiết bị mất CẢ HAI đường; HTTPS trả 401 nên
    ///     nó không provision lại được. <b>Không tự lành</b> — phải mang laptop/điện thoại tới nạp lại.
    ///   </description></item>
    ///   <item><description>
    ///     <c>/rotate-mqtt</c> chỉ đổi mật khẩu MQTT ⇒ broker từ chối (<c>state=4</c>), thiết bị đếm
    ///     đủ ngưỡng thì tự gọi <c>/provision</c> bằng apiKey cũ còn hiệu lực và nhận mật khẩu mới.
    ///     <b>Tự lành trong vài phút.</b>
    ///   </description></item>
    /// </list>
    /// <para>
    /// ⇒ Nghi ngờ lộ credential MQTT thì dùng endpoint NÀY. Chỉ dùng <c>/rotate-key</c> khi chính
    /// <c>apiKey</c> bị lộ, và khi đó đã chấp nhận phải ra hiện trường.
    /// </para>
    /// <para>
    /// Response trả <c>rawApiKey</c> là khoá <b>đang dùng</b> (không đổi) để admin khỏi tưởng bị mất.
    /// </para>
    /// </remarks>
    /// <response code="200">Xoay thành công; thiết bị sẽ tự re-provision.</response>
    /// <response code="401">Chưa đăng nhập / token hết hạn.</response>
    /// <response code="403">Không có role Admin.</response>
    /// <response code="404">Không tìm thấy device.</response>
    [HttpPost("{id:guid}/rotate-mqtt")]
    [Authorize(Roles = "Admin,Staff")]
    [ProducesResponseType(typeof(CommonResponse<IotDeviceCreatedDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<IotDeviceCreatedDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RotateMqttCredential(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new RotateIotDeviceMqttCredentialCommand { Id = id }, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Revoke API key — block device khỏi mọi request (ingest, heartbeat, OTA).
    /// </summary>
    /// <remarks>
    /// Cách hoạt động:
    /// <list type="bullet">
    ///   <item><description>Tìm device; 404 nếu không có.</description></item>
    ///   <item><description>Set <c>ApiKeyRevokedAt = UtcNow</c> + <c>Status = Disabled</c>.</description></item>
    ///   <item><description>Sau revoke: <c>FindDeviceByRawKeyAsync</c> trả null → mọi request từ device này bị reject 401 ở auth handler.</description></item>
    /// </list>
    ///
    /// Use case:
    /// <list type="bullet">
    ///   <item><description>Device bị mất cắp / thanh lý nhanh, chưa kịp <c>DELETE</c>.</description></item>
    ///   <item><description>Bị phát hiện gửi data bất thường, cần đóng ngay không rotate.</description></item>
    /// </list>
    ///
    /// Lưu ý:
    /// <list type="bullet">
    ///   <item><description>Khác <c>DELETE</c>: revoke chỉ chặn auth, device vẫn còn trong list (Status = Disabled) — có thể audit + rotate-key để khôi phục.</description></item>
    ///   <item><description><c>iot_device_heartbeats</c> đã có vẫn giữ nguyên.</description></item>
    ///   <item><description>Để hoàn toàn loại bỏ device, gọi <c>DELETE</c> sau khi đã revoke.</description></item>
    /// </list>
    /// </remarks>
    /// <param name="id">Id device.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> thông báo revoke thành công.</returns>
    /// <response code="200">Revoke thành công.</response>
    /// <response code="401">Chưa đăng nhập / token hết hạn.</response>
    /// <response code="403">Không có role Admin.</response>
    /// <response code="404">Không tìm thấy device.</response>
    [HttpPost("{id:guid}/revoke-key")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeKey(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new RevokeIotDeviceApiKeyCommand { Id = id }, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Sprint IoT-2 #IoT2-25 (S4-BE-05) — push downlink command tới device qua MQTT topic <c>solar/{deviceCode}/cmd</c>.
    /// Device sẽ ack qua topic <c>solar/{deviceCode}/cmd/ack</c> (bridge log lại).
    /// </summary>
    /// <remarks>
    /// Body: <c>{ cmdId, type, params }</c>. Backend không validate sâu — chỉ relay JSON xuống MQTT.
    /// MQTT bridge phải đang chạy (Mqtt:Enabled=true), nếu không sẽ trả 503.
    /// </remarks>
    [HttpPost("{id:guid}/command")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(CommonResponse<IotDeviceCommandAcceptedDto>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> SendCommand(Guid id, [FromBody] IotDeviceCommandPayloadDto body, CancellationToken ct)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.Type))
        {
            return BadRequest(new CommonResponse<object>
            {
                IsSuccess = false,
                StatusCode = 400,
                Message = "Invalid command body.",
                ListErrors = new() { new() { Field = nameof(body.Type), Detail = "Type is required." } }
            });
        }

        var device = await _unitOfWork.IotDevices.GetAllAsync()
            .FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted, ct);
        if (device is null)
        {
            return NotFound(new CommonResponse<object>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Device not found."
            });
        }

        var cmdId = string.IsNullOrWhiteSpace(body.CmdId) ? Guid.NewGuid().ToString("N") : body.CmdId;
        if (await _unitOfWork.IotDeviceCommands.GetAllAsync()
                .AnyAsync(command => command.CmdId == cmdId && !command.IsDeleted, ct))
        {
            return Conflict(new CommonResponse<object>
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "CmdId đã tồn tại."
            });
        }

        Guid? issuedBy = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var accountId)
            ? accountId
            : null;
        var now = DateTime.UtcNow;
        var command = new IotDeviceCommand
        {
            Id = Guid.NewGuid(),
            IotDeviceId = device.Id,
            CmdId = cmdId,
            Type = body.Type,
            ParamsJson = JsonSerializer.Serialize(body.Params ?? new { }),
            Status = IotDeviceCommandStatusEnum.Pending,
            IssuedByAccountId = issuedBy,
            CreatedBy = issuedBy,
            CreatedAt = now
        };
        await _unitOfWork.IotDeviceCommands.AddAsync(command);
        await _unitOfWork.SaveChangesAsync(ct);

        var payload = JsonSerializer.Serialize(new
        {
            cmdId,
            type = body.Type,
            @params = body.Params,
            issuedAt = now
        });

        try
        {
            await _mqttPublisher.PublishCommandAsync(device.DeviceCode, payload, ct);
        }
        catch (Exception ex)
        {
            command.Status = IotDeviceCommandStatusEnum.Failed;
            command.AckError = $"MQTT bridge không khả dụng: {ex.Message}";
            command.AckedAt = DateTime.UtcNow;
            _unitOfWork.IotDeviceCommands.UpdateAsync(command);
            await _unitOfWork.SaveChangesAsync(ct);
            return StatusCode(503, new CommonResponse<object>
            {
                IsSuccess = false,
                StatusCode = 503,
                Message = $"MQTT bridge is unavailable: {ex.Message}"
            });
        }

        return Accepted(new CommonResponse<IotDeviceCommandAcceptedDto>
        {
            IsSuccess = true,
            StatusCode = 202,
            Message = "Command has been enqueued to MQTT.",
            Data = new IotDeviceCommandAcceptedDto
            {
                CmdId = cmdId,
                DeviceCode = device.DeviceCode,
                // IOT3-14 — phải là topic THẬT đã publish, không phải chuỗi dựng lại tay.
                // Trước đây chỗ này nội suy `device.DeviceCode` (UPPERCASE) trong khi
                // MqttBridgeBackgroundService publish qua MqttTopicMap.Command() — nay đã
                // chuẩn hoá chữ thường. Hai chuỗi lệch nhau làm admin debug sai hướng.
                Topic = MqttTopicMap.Command(device.DeviceCode)
            }
        });
    }
}
