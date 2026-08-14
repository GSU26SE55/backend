using BatteryService.Api.Authentication;
using BatteryService.Application.CQRS.Command.EnvironmentalIncident;
using BatteryService.Application.CQRS.Query.EnvironmentalIncident;
using BatteryService.Application.DTOs;
using BatteryService.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedInfrastructure.Services;

namespace BatteryService.Api.Controllers;

/// <summary>
/// Sprint 5B #102/#103 — environmental incident lifecycle endpoints.
/// </summary>
/// <remarks>
/// Nhóm endpoint quản lý vòng đời <see cref="BatteryService.Domain.Entities.EnvironmentalIncident"/> (smoke, fire, gas leak, flood, overheat hazard) ở cấp <b>site</b> —
/// tách hoàn toàn khỏi <see cref="BatteryService.Domain.Entities.Alert"/> cấp battery, nhưng Alert có thể reference incident qua <c>EnvironmentalIncidentId</c>.
///
/// Lifecycle state:
/// <list type="bullet">
///   <item><description><c>Open</c>: incident vừa được report (IoT hoặc operator).</description></item>
///   <item><description><c>Acknowledged</c>: kỹ thuật viên đã xác nhận, đang xử lý.</description></item>
///   <item><description><c>Resolved</c>: đã xử lý xong, kèm <c>ResolutionNote</c>.</description></item>
///   <item><description><c>FalseAlarm</c>: xác định không phải incident thật, kèm <c>FalseAlarmReason</c>.</description></item>
/// </list>
///
/// Phân quyền:
/// <list type="bullet">
///   <item><description><b>ApiKey + scope <c>EnvironmentalIngest</c></b>: report incident từ IoT.</description></item>
///   <item><description><b>Admin/Manager/Staff</b>: acknowledge + resolve.</description></item>
///   <item><description><b>Admin/Manager</b>: mark false alarm.</description></item>
///   <item><description><b>Admin/Manager/Staff/Customer</b>: đọc danh sách + detail.</description></item>
/// </list>
/// </remarks>
[ApiController]
[Route("api/environmental-incidents")]
[Produces("application/json")]
public class EnvironmentalIncidentsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentUserService _currentUser;

    public EnvironmentalIncidentsController(IMediator mediator, ICurrentUserService currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Report incident mới từ IoT edge device hoặc Staff thủ công (SmokeDetected/WaterLeak/Overheating) — tạo Alert(Critical) + publish event tới NotificationService (bypass quiet hours).
    /// </summary>
    /// <remarks>
    /// Authorize bằng <c>ApiKey</c> scheme + policy <c>EnvironmentalIngest</c>.
    ///
    /// Cách hoạt động:
    /// - Validate <c>SiteId</c> tồn tại.
    /// - Tạo incident với <c>Status = Open</c>, <c>DetectedAt</c> mặc định <c>UtcNow</c>.
    /// - Phát <c>EnvironmentalIncidentRaisedEvent</c> để Notification + Ticket service consume.
    /// </remarks>
    /// <param name="cmd">Thông tin incident.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="201">Report thành công, trả id incident mới tạo.</response>
    /// <response code="200">Đã có incident active đang mở — trả về incident cũ thay vì tạo mới (dedup).</response>
    /// <response code="400">Site không tồn tại hoặc payload thiếu field.</response>
    /// <response code="401">Thiếu ApiKey.</response>
    /// <response code="403">ApiKey không có scope <c>EnvironmentalIngest</c>.</response>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    // Trước: [Authorize(..., Policy = "EnvironmentalIngest")] — named policy chưa đăng ký → 500.
    // Đổi sang attribute scope-check chuẩn (giống SensorReadings/IotDevices/Ambient).
    [Authorize(AuthenticationSchemes = ApiKeyAuthenticationHandler.SchemeName)]
    [IotApiKeyScopeRequirement(IotApiKeyScopeEnum.EnvironmentalIngest)]
    public async Task<IActionResult> Report([FromBody] ReportEnvironmentalIncidentCommand cmd, CancellationToken ct)
    {
        // GH-806 — site LẤY TỪ CLAIM của thiết bị đã xác thực, không tin SiteId trong body.
        cmd.AuthenticatedDeviceSiteId = ReadDeviceSiteIdClaim();

        var result = await _mediator.Send(cmd, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Sprint Bonus NS-23 (#663, E3) — report sự cố môi trường THỦ CÔNG bằng JWT (Admin/Manager/Staff), cho con người tạo incident khi thấy cháy/khói/ngập mà không có sensor tự động.
    /// </summary>
    /// <remarks>
    /// Trước fix, <c>POST /</c> chỉ nhận ApiKey (IoT) → Staff đứng tại site thấy cháy KHÔNG có cách tạo incident; đặc biệt loại <c>FireDetected</c>/<c>OverheatHazard</c>/<c>Other</c> (không có sensor) thực tế không ai tạo được.
    ///
    /// Body (giống <c>POST /</c>):
    /// - <c>siteId</c> (Guid, bắt buộc) — site xảy ra sự cố.
    /// - <c>incidentType</c> (<c>EnvironmentalIncidentTypeEnum</c>, bắt buộc): 1 Smoke · 2 FireDetected · 3 GasLeak · 4 Flood · 5 OverheatHazard · 99 Other.
    /// - <c>severity</c> (<c>AlertSeverityEnum</c>, mặc định 3 Critical): 1 Info · 2 Warning · 3 Critical.
    /// - <c>notes</c> (string?, ≤ 1000 ký tự) — mô tả.
    ///
    /// Khác biệt so với <c>POST /</c> (ApiKey IoT):
    /// - <c>reportedBy</c> trong body <b>bị bỏ qua</b> — backend luôn lấy user id từ token (chống mạo danh).
    /// - <c>detectedAt</c> <b>không bắt buộc</b> — mặc định <c>UtcNow</c> nếu bỏ trống.
    ///
    /// Cách hoạt động: reuse cùng handler với <c>POST /</c> → tạo Alert site-level + publish <c>EnvironmentalIncidentDetectedEvent</c> → NotificationService notify + TicketService auto-tạo ticket P1 (NS-22). Dedup: đã có incident active <c>Open</c>/<c>Acknowledged</c> cùng <c>SiteId</c>+<c>IncidentType</c> → trả incident cũ (200), KHÔNG phát event lần nữa.
    /// </remarks>
    /// <param name="cmd">Thông tin incident. <c>reportedBy</c> bị ghi đè từ token; <c>detectedAt</c> optional.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa <c>EnvironmentalIncidentDto</c> (incident mới tạo hoặc incident cũ nếu dedup).</returns>
    /// <response code="201">Report thành công — incident mới tạo (Status Open).</response>
    /// <response code="200">Đã có incident active cùng site+loại — trả incident cũ (dedup, không phát event lại).</response>
    /// <response code="400">Payload thiếu/không hợp lệ — <c>SiteId</c> rỗng, <c>IncidentType</c>/<c>Severity</c> sai (field-level <c>listErrors</c>).</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin/Manager/Staff (Customer bị chặn).</response>
    [HttpPost("manual")]
    [ProducesResponseType(typeof(EnvironmentalIncidentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(EnvironmentalIncidentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(EnvironmentalIncidentResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Roles = "Admin,Manager,Staff")]
    public async Task<IActionResult> ReportManual([FromBody] ReportEnvironmentalIncidentCommand cmd, CancellationToken ct)
    {
        // ReportedBy lấy từ token — không tin field client gửi (chống mạo danh).
        cmd.ReportedBy = _currentUser.UserId;
        if (cmd.DetectedAt == default)
            cmd.DetectedAt = DateTime.UtcNow;

        var result = await _mediator.Send(cmd, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Acknowledge incident — chuyển state <c>Open → Acknowledged</c>.
    /// </summary>
    /// <remarks>
    /// Chỉ áp dụng khi state hiện tại là <c>Open</c>. Set <c>AcknowledgedBy</c> = user hiện tại + <c>AcknowledgedAt = UtcNow</c>.
    /// </remarks>
    /// <param name="id">Id incident.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Acknowledge thành công.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin/Manager/Staff.</response>
    /// <response code="404">Incident không tồn tại.</response>
    /// <response code="409">State hiện tại không cho phép acknowledge.</response>
    [HttpPost("{id:guid}/acknowledge")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Roles = "Admin,Manager,Staff")]
    public async Task<IActionResult> Acknowledge(Guid id, CancellationToken ct)
    {
        var actor = Guid.TryParse(_currentUser.UserId, out var u) ? u : Guid.Empty;
        var result = await _mediator.Send(new AcknowledgeEnvironmentalIncidentCommand { Id = id, AcknowledgedBy = actor }, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Resolve incident — chuyển state <c>Open|Acknowledged → Resolved</c>.
    /// </summary>
    /// <remarks>
    /// Bắt buộc kèm <c>resolutionNote</c> mô tả cách xử lý (audit trail).
    /// Set <c>ResolvedBy</c> = user hiện tại + <c>ResolvedAt = UtcNow</c>.
    /// </remarks>
    /// <param name="id">Id incident.</param>
    /// <param name="body">Body chứa <c>resolutionNote</c>.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Resolve thành công.</response>
    /// <response code="400">Body thiếu hoặc <c>ResolutionNote</c> không hợp lệ.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin/Manager/Staff.</response>
    /// <response code="404">Incident không tồn tại.</response>
    /// <response code="409">State không cho phép resolve.</response>
    [HttpPost("{id:guid}/resolve")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Roles = "Admin,Manager,Staff")]
    public async Task<IActionResult> Resolve(Guid id, [FromBody] ResolveEnvironmentalIncidentRequest body, CancellationToken ct)
    {
        var actor = Guid.TryParse(_currentUser.UserId, out var u) ? u : Guid.Empty;
        var result = await _mediator.Send(new ResolveEnvironmentalIncidentCommand
        {
            Id = id,
            ResolvedBy = actor,
            ResolutionNote = body.ResolutionNote
        }, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Đánh dấu incident là FalseAlarm (không phải sự cố thật) — set Status=FalseAlarm + WasFalseAlarm=true, publish EnvironmentalIncidentResolvedEvent để clear in-app banner.
    /// </summary>
    /// <remarks>
    /// Chỉ Admin/Manager được phép đánh dấu false alarm để tránh lạm dụng.
    /// Bắt buộc kèm <c>falseAlarmReason</c> để audit. State có thể bất kỳ trừ <c>Resolved</c>.
    /// </remarks>
    /// <param name="id">Id incident.</param>
    /// <param name="body">Body chứa <c>falseAlarmReason</c>.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Mark thành công.</response>
    /// <response code="400">Body thiếu hoặc <c>FalseAlarmReason</c> không hợp lệ.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin/Manager.</response>
    /// <response code="404">Incident không tồn tại.</response>
    /// <response code="409">State đang là Resolved.</response>
    [HttpPost("{id:guid}/false-alarm")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> FalseAlarm(Guid id, [FromBody] FalseAlarmEnvironmentalIncidentRequest body, CancellationToken ct)
    {
        var actor = Guid.TryParse(_currentUser.UserId, out var u) ? u : Guid.Empty;
        var result = await _mediator.Send(new MarkFalseAlarmEnvironmentalIncidentCommand
        {
            Id = id,
            FalseAlarmBy = actor,
            FalseAlarmReason = body.FalseAlarmReason
        }, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Liệt kê incident (filter theo siteId/status/incidentType/time range) + phân trang — sort theo DetectedAt DESC; admin/manager review historical incidents.
    /// </summary>
    /// <remarks>
    /// Query parameters hỗ trợ filter theo <c>SiteId</c>, <c>Status</c>, <c>IncidentType</c>, <c>Severity</c>, date range.
    /// Sort mặc định theo <c>DetectedAt DESC</c>. Filter <c>!IsDeleted</c>.
    /// </remarks>
    /// <param name="query">Filter + phân trang.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Trả danh sách incident.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Roles = "Admin,Manager,Staff,Customer")]
    public async Task<IActionResult> GetList([FromQuery] GetEnvironmentalIncidentsQuery query, CancellationToken ct)
    {
        var result = await _mediator.Send(query, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Lấy chi tiết 1 incident theo id — full metadata + resolution note + acknowledged/resolved user info + linked Alert.
    /// </summary>
    /// <remarks>
    /// Trả đầy đủ thông tin lifecycle (Acknowledged/Resolved/FalseAlarm actor + timestamps) và danh sách <c>Alert</c> liên quan.
    /// </remarks>
    /// <param name="id">Id incident.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Trả chi tiết incident.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="404">Incident không tồn tại.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Roles = "Admin,Manager,Staff,Customer")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetEnvironmentalIncidentByIdQuery { Id = id }, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Liệt kê incident đang Active (<c>Open</c> hoặc <c>Acknowledged</c>) theo site.
    /// </summary>
    /// <remarks>
    /// Dùng cho dashboard site — Customer xem nhanh các incident đang xử lý tại site của mình.
    /// </remarks>
    /// <param name="siteId">Id site.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Trả danh sách incident active.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    [HttpGet("by-site/{siteId:guid}/active")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Roles = "Admin,Manager,Staff,Customer")]
    public async Task<IActionResult> ActiveBySite(Guid siteId, CancellationToken ct)
    {
        var result = await _mediator.Send(new ActiveEnvironmentalIncidentsBySiteQuery { SiteId = siteId }, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// GH-806 — đọc claim <c>iot:site_id</c> do <c>ApiKeyAuthenticationHandler</c> phát ra.
    /// Trả <c>null</c> khi người gọi là con người dùng JWT (endpoint report thủ công) — lúc đó chỉ
    /// còn kiểm tồn tại site.
    /// </summary>
    private Guid? ReadDeviceSiteIdClaim()
    {
        var raw = User.FindFirst(ApiKeyAuthenticationHandler.ClaimDeviceSiteId)?.Value;
        return Guid.TryParse(raw, out var siteId) ? siteId : null;
    }
}
