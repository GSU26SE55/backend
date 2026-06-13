using BatteryService.Application.CQRS.Command.EnvironmentalIncident;
using BatteryService.Application.CQRS.Query.EnvironmentalIncident;
using BatteryService.Application.DTOs;
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
    /// Report incident mới từ IoT edge.
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
    /// <response code="200">Report thành công, trả id incident.</response>
    /// <response code="400">Site không tồn tại hoặc payload thiếu field.</response>
    /// <response code="401">Thiếu ApiKey.</response>
    /// <response code="403">ApiKey không có scope <c>EnvironmentalIngest</c>.</response>
    [HttpPost]
    [Authorize(AuthenticationSchemes = "ApiKey", Policy = "EnvironmentalIngest")]
    public async Task<IActionResult> Report([FromBody] ReportEnvironmentalIncidentCommand cmd, CancellationToken ct)
    {
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
    /// Đánh dấu <c>FalseAlarm</c> — incident không thật.
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
    /// Liệt kê incident (filter + phân trang).
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
    [Authorize(Roles = "Admin,Manager,Staff,Customer")]
    public async Task<IActionResult> GetList([FromQuery] GetEnvironmentalIncidentsQuery query, CancellationToken ct)
    {
        var result = await _mediator.Send(query, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Chi tiết incident theo id.
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
    [Authorize(Roles = "Admin,Manager,Staff,Customer")]
    public async Task<IActionResult> ActiveBySite(Guid siteId, CancellationToken ct)
    {
        var result = await _mediator.Send(new ActiveEnvironmentalIncidentsBySiteQuery { SiteId = siteId }, ct);
        return StatusCode(result.StatusCode, result);
    }
}
