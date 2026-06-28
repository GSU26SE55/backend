using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharedContracts.Common.Responses;
using SharedInfrastructure.Services;
using TicketService.Application.CQRS.Query.Reports;
using TicketService.Application.DTOs.Response.Reports;

namespace TicketService.Api.Controllers;

/// <summary>
/// Sprint 7 #114 (§5.2) — <b>TicketService reports</b> (9 endpoint).
/// </summary>
/// <remarks>
/// <b>Export:</b> mọi report nhận <c>?format=csv</c>/<c>xlsx</c> → trả file download (XLSX dùng ClosedXML).
/// Không có <c>format</c> → JSON <see cref="CommonResponse{T}"/>.
///
/// <b>Thời gian:</b> <c>from</c>/<c>to</c> UTC tùy chọn; time-series mặc định 30 ngày nếu trống;
/// <c>granularity</c>: <c>day</c>/<c>week</c>/<c>month</c>.
///
/// <b>SLA:</b> "met" / "breached" lấy từ <c>SlaTimerStatusEnum</c> (Met/Breached); "resolved" =
/// Status ∈ {Resolved, ClosedPendingRate, Closed}.
///
/// <b>Quyền:</b> Admin/Manager cho mọi report; riêng <c>saga-failed-rate</c> chỉ Admin (SRE demo).
/// Route phẳng <c>api/reports/...</c>.
/// </remarks>
[ApiController]
[Produces("application/json")]
[Authorize(Roles = "Admin,Manager")]
public class ReportsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ReportsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>SLA compliance theo từng staff: tổng assigned, met, breached, tỷ lệ tuân thủ.</summary>
    /// <remarks>Filter <c>from</c>/<c>to</c>. Hỗ trợ <c>?format=csv|xlsx</c>.</remarks>
    /// <response code="200">Danh sách theo staff, hoặc file nếu có <c>format</c>.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role phù hợp.</response>
    [HttpGet("api/reports/sla-by-staff")]
    [ProducesResponseType(typeof(CommonResponse<List<SlaByStaffRow>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SlaByStaff([FromQuery] SlaByStaffReportQuery query, [FromQuery] string? format, CancellationToken ct)
        => Respond(await _mediator.Send(query, ct), format, TicketReportTables.SlaByStaff);

    /// <summary>SLA compliance theo priority (P1/P2/P3): met/breached/total + tỷ lệ.</summary>
    /// <remarks>Filter <c>from</c>/<c>to</c>. Luôn trả đủ 3 row P1/P2/P3. Hỗ trợ <c>?format=csv|xlsx</c>.</remarks>
    /// <response code="200">3 row priority, hoặc file nếu có <c>format</c>.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role phù hợp.</response>
    [HttpGet("api/reports/sla-by-priority")]
    [ProducesResponseType(typeof(CommonResponse<List<SlaByPriorityRow>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SlaByPriority([FromQuery] SlaByPriorityReportQuery query, [FromQuery] string? format, CancellationToken ct)
        => Respond(await _mediator.Send(query, ct), format, TicketReportTables.SlaByPriority);

    /// <summary>Số lượng ticket tạo mới theo thời gian (time-series).</summary>
    /// <remarks>Filter <c>from</c>/<c>to</c> + <c>granularity</c> (mặc định 30 ngày). Hỗ trợ <c>?format=csv|xlsx</c>.</remarks>
    /// <response code="200">Time-series, hoặc file nếu có <c>format</c>.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role phù hợp.</response>
    [HttpGet("api/reports/ticket-volume")]
    [ProducesResponseType(typeof(CommonResponse<List<TicketTimeSeriesPoint>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> TicketVolume([FromQuery] TicketVolumeReportQuery query, [FromQuery] string? format, CancellationToken ct)
        => Respond(await _mediator.Send(query, ct), format, data => TicketReportTables.TimeSeries("ticket-volume", data));

    /// <summary>Top issue bị reopen nhiều theo category (count + số reopen trung bình).</summary>
    /// <remarks>Tham số <c>limit</c> (mặc định 10, tối đa 100). Chỉ tính ticket có ReopenCount &gt; 0. Hỗ trợ <c>?format=csv|xlsx</c>.</remarks>
    /// <response code="200">Top reopen, hoặc file nếu có <c>format</c>.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role phù hợp.</response>
    [HttpGet("api/reports/top-reopen-issues")]
    [ProducesResponseType(typeof(CommonResponse<List<ReopenIssueRow>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> TopReopenIssues([FromQuery] TopReopenIssuesReportQuery query, [FromQuery] string? format, CancellationToken ct)
        => Respond(await _mediator.Send(query, ct), format, TicketReportTables.TopReopen);

    /// <summary>Hiệu suất staff: số ticket resolved, giờ xử lý TB, rating TB, % SLA compliance.</summary>
    /// <remarks>Filter <c>from</c>/<c>to</c>. Hỗ trợ <c>?format=csv|xlsx</c>.</remarks>
    /// <response code="200">Hiệu suất theo staff, hoặc file nếu có <c>format</c>.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role phù hợp.</response>
    [HttpGet("api/reports/staff-performance")]
    [ProducesResponseType(typeof(CommonResponse<List<StaffPerformanceRow>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> StaffPerformance([FromQuery] StaffPerformanceReportQuery query, [FromQuery] string? format, CancellationToken ct)
        => Respond(await _mediator.Send(query, ct), format, TicketReportTables.StaffPerformance);

    /// <summary>CSAT — chỉ số hài lòng: rating trung bình, tổng số rated, phân bố 1..5 sao.</summary>
    /// <remarks>Filter <c>from</c>/<c>to</c> (theo RatedAt). Export flatten metric + phân bố thành bảng. Hỗ trợ <c>?format=csv|xlsx</c>.</remarks>
    /// <response code="200">CSAT object, hoặc file nếu có <c>format</c>.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role phù hợp.</response>
    [HttpGet("api/reports/csat")]
    [ProducesResponseType(typeof(CommonResponse<CsatDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Csat([FromQuery] CsatReportQuery query, [FromQuery] string? format, CancellationToken ct)
    {
        var res = await _mediator.Send(query, ct);
        if (res.IsSuccess && res.Data is not null && ReportFileExporter.IsExportFormat(format))
        {
            var file = ReportFileExporter.Export(TicketReportTables.Csat(res.Data), format);
            return File(file.Content, file.ContentType, file.FileName);
        }
        return StatusCode(res.StatusCode, res);
    }

    /// <summary>Histogram thời gian resolution (0-1h / 1-4h / 4-24h / 1-3d / &gt;3d).</summary>
    /// <remarks>Chỉ tính ticket đã có ResolvedAt; filter <c>from</c>/<c>to</c>. Hỗ trợ <c>?format=csv|xlsx</c>.</remarks>
    /// <response code="200">Buckets histogram, hoặc file nếu có <c>format</c>.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role phù hợp.</response>
    [HttpGet("api/reports/resolution-time-histogram")]
    [ProducesResponseType(typeof(CommonResponse<List<HistogramBucketRow>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ResolutionTimeHistogram([FromQuery] ResolutionTimeHistogramReportQuery query, [FromQuery] string? format, CancellationToken ct)
        => Respond(await _mediator.Send(query, ct), format, TicketReportTables.Histogram);

    /// <summary>Phân bố ticket theo category (count, sort giảm dần).</summary>
    /// <remarks>Filter <c>from</c>/<c>to</c>. Hỗ trợ <c>?format=csv|xlsx</c>.</remarks>
    /// <response code="200">Phân bố category, hoặc file nếu có <c>format</c>.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role phù hợp.</response>
    [HttpGet("api/reports/category-breakdown")]
    [ProducesResponseType(typeof(CommonResponse<List<CategoryBreakdownRow>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CategoryBreakdown([FromQuery] CategoryBreakdownReportQuery query, [FromQuery] string? format, CancellationToken ct)
        => Respond(await _mediator.Send(query, ct), format, TicketReportTables.CategoryBreakdown);

    /// <summary>Saga failed rate theo thời gian: started/completed/failed + tỷ lệ fail + p95 duration (chỉ Admin).</summary>
    /// <remarks>
    /// Đọc <c>AlertTicketSagaState</c>. Filter <c>from</c>/<c>to</c> + <c>granularity</c> (mặc định 30 ngày).
    /// Phục vụ demo SRE practice (SLO) cho hội đồng KLTN. Hỗ trợ <c>?format=csv|xlsx</c>.
    /// </remarks>
    /// <response code="200">Time-series saga, hoặc file nếu có <c>format</c>.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không phải Admin.</response>
    [HttpGet("api/reports/saga-failed-rate")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(CommonResponse<List<SagaFailedRatePoint>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SagaFailedRate([FromQuery] SagaFailedRateReportQuery query, [FromQuery] string? format, CancellationToken ct)
        => Respond(await _mediator.Send(query, ct), format, TicketReportTables.SagaFailedRate);

    private IActionResult Respond<T>(
        CommonResponse<List<T>> res, string? format, Func<List<T>, ReportTable> toTable)
    {
        if (res.IsSuccess && res.Data is not null && ReportFileExporter.IsExportFormat(format))
        {
            var file = ReportFileExporter.Export(toTable(res.Data), format);
            return File(file.Content, file.ContentType, file.FileName);
        }
        return StatusCode(res.StatusCode, res);
    }
}
