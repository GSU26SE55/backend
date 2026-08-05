using BatteryService.Application.CQRS.Command.Alert;
using BatteryService.Application.CQRS.Query.Alert;
using BatteryService.Application.DTOs;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedContracts.Common.Responses;

namespace BatteryService.Api.Controllers;

/// <summary>
/// Nhóm endpoint truy vấn và xử lý <b>Alert</b> - cảnh báo bất thường được sinh ra từ AnomalyDetector khi SensorReading
/// vượt ngưỡng ThresholdConfig hoặc khi thiết bị offline.
/// </summary>
/// <remarks>
/// State machine của Alert:
/// <code>
/// Open (mới phát sinh)
///   ├─→ Acknowledged (Staff/Customer xác nhận đã thấy)
///   │       └─→ Resolved (đã xử lý xong)
///   ├─→ Resolved (resolve trực tiếp không cần ack)
///   └─→ Merged (bị merge vào alert khác trong dedup window - Sprint 3)
/// </code>
///
/// Quy ước phân quyền:
/// <list type="bullet">
///   <item><description><b>List/Detail</b>: tất cả role đăng nhập (Admin/Manager/Staff/Customer).</description></item>
///   <item><description><b>Acknowledge</b>: tất cả role (Customer có thể tự ack khi nhận push notification).</description></item>
///   <item><description><b>Resolve</b>: chỉ Admin/Manager/Staff (Customer KHÔNG được resolve - tránh tự đóng cảnh báo của mình mà không xử lý).</description></item>
/// </list>
///
/// Ở Sprint 3, AnomalyDetector + AlertDeduplicationService sẽ tự động tạo Alert; endpoint POST tạo Alert thủ công
/// chưa được expose.
/// </remarks>
[ApiController]
[Route("api/alerts")]
[Produces("application/json")]
public class AlertsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AlertsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Liệt kê Alert có phân trang + filter (severity/status/anomalyType/assetId/site/time range) — sort theo DetectedAt DESC. Manager/Staff dashboard dùng query này.
    /// </summary>
    /// <remarks>
    /// Query parameters:
    /// - <c>PageNumber</c>, <c>PageSize</c>: phân trang.
    /// - <c>BatteryAssetId</c>: tùy chọn, lọc alert của asset cụ thể.
    /// - <c>Severity</c>: enum <c>AlertSeverityEnum</c> (<c>Info = 1</c>, <c>Warning = 2</c>, <c>Critical = 3</c>).
    /// - <c>Status</c>: enum <c>AlertStatusEnum</c> (<c>Open = 1</c>, <c>Acknowledged = 2</c>, <c>Merged = 3</c>, <c>Resolved = 4</c>).
    /// - <c>From</c>: tùy chọn, lọc <c>DetectedAt &gt;= From</c> (UTC).
    /// - <c>To</c>: tùy chọn, lọc <c>DetectedAt &lt;= To</c> (UTC).
    ///
    /// Cách hoạt động:
    /// - Filter <c>!IsDeleted</c> + các điều kiện optional.
    /// - Include BatteryAsset để DTO có <c>BatterySerialNumber</c>.
    /// - Sort theo <c>DetectedAt</c> giảm dần (alert mới nhất lên đầu).
    ///
    /// Lưu ý phân quyền dữ liệu:
    /// - Endpoint cho phép tất cả role đăng nhập gọi. Hiện <b>chưa</b> có server-side filter để giới hạn Customer
    ///   chỉ thấy alert của asset thuộc mình. FE/Mobile nên truyền <c>BatteryAssetId</c> trỏ về asset của Customer
    ///   để giới hạn dữ liệu.
    /// </remarks>
    /// <param name="query">Filter + phân trang.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa <see cref="PaginationResponse{T}"/> các <see cref="AlertDto"/>.</returns>
    /// <response code="200">Trả danh sách.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role phù hợp.</response>
    [HttpGet]
    [Authorize(Roles = "Admin,Manager,Staff,Customer")]
    [ProducesResponseType(typeof(CommonResponse<PaginationResponse<AlertDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAll([FromQuery] GetAlertsQuery query, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(query, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Lấy chi tiết 1 Alert theo Id — bao gồm BatteryAsset, EnvironmentalIncident link nếu có, AcknowledgedBy/ResolvedBy user info, full activity timeline.
    /// </summary>
    /// <remarks>
    /// Trả về <see cref="AlertDto"/> đầy đủ: thông tin anomaly (type, severity, threshold/actual value, unit, detected time),
    /// trạng thái xử lý (status, acknowledged by/at, resolved at), thông tin merge và liên kết với Ticket nếu có.
    ///
    /// 404 nếu alert không tồn tại hoặc đã bị soft delete (TimescaleDB-related soft delete trên alert, không phải trên reading).
    /// </remarks>
    /// <param name="id">Id Alert.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa <see cref="AlertDto"/>.</returns>
    /// <response code="200">Trả về alert.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role phù hợp.</response>
    /// <response code="404">Alert không tồn tại.</response>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Admin,Manager,Staff,Customer")]
    [ProducesResponseType(typeof(CommonResponse<AlertDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<AlertDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetAlertByIdQuery { Id = id }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Xác nhận đã nhìn thấy/tiếp nhận 1 Alert — set Status=Acknowledged + AcknowledgedAt + AcknowledgedByUserId. SLA timer chuyển sang phase next.
    /// </summary>
    /// <remarks>
    /// Endpoint chuyển trạng thái: <c>Open → Acknowledged</c>.
    ///
    /// Cách hoạt động:
    /// - Tìm alert; 404 nếu không có / đã xóa.
    /// - Nếu alert đang ở <c>Resolved</c> hoặc <c>Merged</c>, trả 409 (không ack được nữa).
    /// - Set <c>Status = Acknowledged</c>, <c>AcknowledgedAt = UtcNow</c>.
    /// - Nếu token có claim UserId hợp lệ, set <c>AcknowledgedByUserId</c>; nếu không có cũng OK (vẫn cho ack vô danh, dù không nên).
    ///
    /// Use case:
    /// - Customer nhận push notification về anomaly, mở app, bấm "Đã hiểu" → ack.
    /// - Staff thấy alert trong dashboard, bấm ack trước khi đi xử lý hiện trường.
    ///
    /// Lưu ý:
    /// - Ack không phải resolve - alert vẫn cần được xử lý và resolve sau đó.
    /// - Việc ack KHÔNG dừng SLA timer của Ticket liên kết (nếu có).
    /// </remarks>
    /// <param name="id">Id Alert.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> thông báo ack thành công.</returns>
    /// <response code="200">Ack thành công.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role phù hợp.</response>
    /// <response code="404">Alert không tồn tại.</response>
    /// <response code="409">Alert đã ở trạng thái Resolved hoặc Merged.</response>
    [HttpPatch("{id:guid}/acknowledge")]
    [Authorize(Roles = "Admin,Manager,Staff,Customer")]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Acknowledge(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new AcknowledgeAlertCommand { Id = id }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Đóng 1 Alert (đã xử lý xong) — set Status=Resolved + ResolvedAt. Customer được notify; KHÔNG tạo Ticket nếu chưa có (Saga đã handle ở giai đoạn Detected).
    /// </summary>
    /// <remarks>
    /// Endpoint chuyển trạng thái: <c>Open</c> hoặc <c>Acknowledged → Resolved</c>.
    ///
    /// Cách hoạt động:
    /// - Tìm alert; 404 nếu không có.
    /// - Nếu alert đang ở <c>Merged</c>, trả 409 (alert đã bị merge vào alert khác, không thể resolve trực tiếp - phải resolve alert gốc).
    /// - Set <c>Status = Resolved</c>, <c>ResolvedAt = UtcNow</c>.
    ///
    /// Use case:
    /// - Staff xử lý xong hiện trường (thay pin, sửa cáp, reset thiết bị) → resolve.
    /// - Manager review false-positive → resolve để dọn dashboard.
    ///
    /// Lưu ý:
    /// - Resolve KHÔNG tự đóng ticket liên kết; phải đóng ticket riêng qua TicketService (Sprint 4).
    /// - Sau khi resolved, alert vẫn xuất hiện trong list (chỉ filter Status mới ẩn được). Để xóa hoàn toàn, dùng soft delete (chưa expose qua API).
    /// </remarks>
    /// <param name="id">Id Alert.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> thông báo resolve thành công.</returns>
    /// <response code="200">Resolve thành công.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin/Manager/Staff.</response>
    /// <response code="404">Alert không tồn tại.</response>
    /// <response code="409">Alert đã ở trạng thái Merged.</response>
    [HttpPatch("{id:guid}/resolve")]
    [Authorize(Roles = "Admin,Manager,Staff")]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Resolve(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ResolveAlertCommand { Id = id }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// GH-778 — kỹ thuật viên phản hồi về prescription AI đã đưa cho alert này
    /// (<c>accepted</c> / <c>edited</c> / <c>rejected</c>).
    /// </summary>
    /// <remarks>
    /// Prescription được chấp nhận sẽ thành ví dụ few-shot cho các ca tương tự sau. Trước GH-778
    /// <c>prescription_id</c> bị bỏ ngay lúc map response nên vòng học không bao giờ khép lại.
    /// <para>
    /// Mã trả về: <b>409</b> alert có thật nhưng chưa có prescription để phản hồi ·
    /// <b>410</b> AI không còn giữ id đó (thử lại vô ích) · <b>503</b> AI không kết nối được (thử lại sau).
    /// Ba ca này cố ý tách nhau để client biết khi nào NÊN retry.
    /// </para>
    /// Customer chỉ phản hồi được alert của mình; alert của khách khác trả 404 (không phải 403 —
    /// 403 sẽ xác nhận alert đó có thật).
    /// </remarks>
    [HttpPost("{id:guid}/prescription-feedback")]
    [Authorize(Roles = "Admin,Manager,Staff,Customer")]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status410Gone)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> SubmitPrescriptionFeedback(
        Guid id,
        [FromBody] SubmitPrescriptionFeedbackCommand command,
        CancellationToken cancellationToken)
    {
        command.AlertId = id;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}
