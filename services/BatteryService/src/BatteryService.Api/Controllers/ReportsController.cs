using BatteryService.Application.CQRS.Query.Report;
using BatteryService.Application.DTOs.Reports;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharedContracts.Common.Responses;
using SharedInfrastructure.Services;

namespace BatteryService.Api.Controllers;

/// <summary>
/// Sprint 7 #114 (§5.2) — <b>BatteryService reports</b> (7 endpoint).
/// </summary>
/// <remarks>
/// <b>Export:</b> mọi report nhận query <c>?format=csv</c> hoặc <c>?format=xlsx</c> → trả file download
/// (XLSX dùng ClosedXML; CSV có UTF-8 BOM cho tiếng Việt). Không có <c>format</c> (hoặc giá trị khác)
/// → trả JSON <see cref="CommonResponse{T}"/>.
///
/// <b>Thời gian:</b> các tham số <c>from</c>/<c>to</c> là UTC, tùy chọn; report time-series mặc định 30 ngày
/// gần nhất nếu bỏ trống. <c>granularity</c>: <c>day</c> (mặc định) · <c>week</c> · <c>month</c>.
///
/// <b>Quyền:</b> Admin/Manager cho mọi report quản trị; riêng <c>ambient-trend</c> mở thêm Staff/Customer.
/// Route phẳng <c>api/reports/...</c> (project bỏ API versioning).
/// </remarks>
[ApiController]
[Produces("application/json")]
public class ReportsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ReportsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Sức khỏe pin theo loại: tổng asset, số có alert active, health score (% asset không có alert).</summary>
    /// <remarks>Hỗ trợ <c>?format=csv|xlsx</c>. Không có filter thời gian.</remarks>
    /// <response code="200">JSON danh sách, hoặc file CSV/XLSX nếu có <c>format</c>.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role phù hợp.</response>
    [HttpGet("api/reports/battery-health-by-type")]
    [Authorize(Roles = "Admin,Manager")]
    [ProducesResponseType(typeof(CommonResponse<List<BatteryHealthByTypeRow>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> BatteryHealthByType([FromQuery] string? format, CancellationToken ct)
    {
        var res = await _mediator.Send(new BatteryHealthByTypeReportQuery(), ct);
        return Respond(res, format, BatteryReportTables.BatteryHealthByType);
    }

    /// <summary>Số lượng Alert theo thời gian (time-series).</summary>
    /// <remarks>Filter <c>from</c>/<c>to</c> (UTC, mặc định 30 ngày) + <c>granularity</c>. Hỗ trợ <c>?format=csv|xlsx</c>.</remarks>
    /// <response code="200">Time-series, hoặc file nếu có <c>format</c>.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role phù hợp.</response>
    [HttpGet("api/reports/alert-volume")]
    [Authorize(Roles = "Admin,Manager")]
    [ProducesResponseType(typeof(CommonResponse<List<ReportTimeSeriesPoint>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> AlertVolume([FromQuery] AlertVolumeReportQuery query, [FromQuery] string? format, CancellationToken ct)
    {
        var res = await _mediator.Send(query, ct);
        return Respond(res, format, data => BatteryReportTables.TimeSeries("alert-volume", data));
    }

    /// <summary>Top loại anomaly theo số lượng (kèm số Critical).</summary>
    /// <remarks>Filter <c>from</c>/<c>to</c> + <c>limit</c> (mặc định 10, tối đa 100). Hỗ trợ <c>?format=csv|xlsx</c>.</remarks>
    /// <response code="200">Danh sách top anomaly, hoặc file nếu có <c>format</c>.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role phù hợp.</response>
    [HttpGet("api/reports/top-anomalies")]
    [Authorize(Roles = "Admin,Manager")]
    [ProducesResponseType(typeof(CommonResponse<List<TopAnomalyRow>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> TopAnomalies([FromQuery] TopAnomaliesReportQuery query, [FromQuery] string? format, CancellationToken ct)
    {
        var res = await _mediator.Send(query, ct);
        return Respond(res, format, BatteryReportTables.TopAnomalies);
    }

    /// <summary>Vòng đời asset: tuổi (ngày), cycle count (BMS), tổng alert.</summary>
    /// <remarks>Không filter thời gian. Hỗ trợ <c>?format=csv|xlsx</c>.</remarks>
    /// <response code="200">Danh sách lifecycle, hoặc file nếu có <c>format</c>.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role phù hợp.</response>
    [HttpGet("api/reports/asset-lifecycle")]
    [Authorize(Roles = "Admin,Manager")]
    [ProducesResponseType(typeof(CommonResponse<List<AssetLifecycleRow>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> AssetLifecycle([FromQuery] string? format, CancellationToken ct)
    {
        var res = await _mediator.Send(new AssetLifecycleReportQuery(), ct);
        return Respond(res, format, BatteryReportTables.AssetLifecycle);
    }

    /// <summary>Asset sắp hết bảo hành trong N ngày.</summary>
    /// <remarks>Tham số <c>within</c> (vd "90d" hoặc "90", mặc định 90 ngày). Hỗ trợ <c>?format=csv|xlsx</c>.</remarks>
    /// <response code="200">Danh sách asset sắp hết hạn, hoặc file nếu có <c>format</c>.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role phù hợp.</response>
    [HttpGet("api/reports/warranty-expiring")]
    [Authorize(Roles = "Admin,Manager")]
    [ProducesResponseType(typeof(CommonResponse<List<WarrantyExpiringRow>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> WarrantyExpiring([FromQuery] WarrantyExpiringReportQuery query, [FromQuery] string? format, CancellationToken ct)
    {
        var res = await _mediator.Send(query, ct);
        return Respond(res, format, BatteryReportTables.WarrantyExpiring);
    }

    /// <summary>Sự cố môi trường theo site/loại/thời gian (Sprint 7 mới).</summary>
    /// <remarks>Filter <c>from</c>/<c>to</c>/<c>siteId</c>/<c>type</c> (EnvironmentalIncidentTypeEnum). Hỗ trợ <c>?format=csv|xlsx</c>.</remarks>
    /// <response code="200">Danh sách sự cố, hoặc file nếu có <c>format</c>.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role phù hợp.</response>
    [HttpGet("api/reports/environmental-incidents")]
    [Authorize(Roles = "Admin,Manager")]
    [ProducesResponseType(typeof(CommonResponse<List<EnvironmentalIncidentRow>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> EnvironmentalIncidents([FromQuery] EnvironmentalIncidentsReportQuery query, [FromQuery] string? format, CancellationToken ct)
    {
        var res = await _mediator.Send(query, ct);
        return Respond(res, format, BatteryReportTables.EnvironmentalIncidents);
    }

    /// <summary>Xu hướng nhiệt độ môi trường theo thời gian cho 1 site (Sprint 7 mới).</summary>
    /// <remarks>
    /// Bắt buộc <c>siteId</c>; filter <c>from</c>/<c>to</c> + <c>granularity</c>. Trả avg/max/min temp, humidity, irradiance theo bucket.
    /// Mở thêm cho Staff/Customer (Customer xem site của mình). Hỗ trợ <c>?format=csv|xlsx</c>.
    /// </remarks>
    /// <response code="200">Time-series ambient, hoặc file nếu có <c>format</c>.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role phù hợp.</response>
    [HttpGet("api/reports/ambient-trend")]
    [Authorize(Roles = "Admin,Manager,Staff,Customer")]
    [ProducesResponseType(typeof(CommonResponse<List<AmbientTrendPoint>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> AmbientTrend([FromQuery] AmbientTrendReportQuery query, [FromQuery] string? format, CancellationToken ct)
    {
        var res = await _mediator.Send(query, ct);
        return Respond(res, format, BatteryReportTables.AmbientTrend);
    }

    /// <summary>JSON khi không có format; file CSV/XLSX khi format hợp lệ và có data.</summary>
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
