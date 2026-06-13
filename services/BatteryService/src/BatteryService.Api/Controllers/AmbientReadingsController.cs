using BatteryService.Application.CQRS.Command.Ambient;
using BatteryService.Application.CQRS.Handler.Ambient;
using BatteryService.Application.CQRS.Query.Ambient;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BatteryService.Api.Controllers;

/// <summary>
/// Sprint 5B #91/#92 — ambient ingestion + history + threshold config (per-site).
/// </summary>
/// <remarks>
/// Nhóm endpoint quản lý dữ liệu môi trường xung quanh site (ambient temperature, humidity, solar irradiance):
/// <list type="bullet">
///   <item><description>Ingest dữ liệu từ IoT edge hoặc OpenMeteo WeatherSync.</description></item>
///   <item><description>Truy vấn lịch sử + latest snapshot cho dashboard.</description></item>
///   <item><description>Quản lý ngưỡng <see cref="BatteryService.Domain.Entities.AmbientThresholdConfig"/> per-site (warning + critical + combo rule).</description></item>
/// </list>
///
/// Phân quyền:
/// <list type="bullet">
///   <item><description><b>ApiKey + scope <c>EnvironmentalIngest</c></b>: batch ingest.</description></item>
///   <item><description><b>Admin/Manager/Staff/Customer</b>: đọc readings.</description></item>
///   <item><description><b>Admin/Manager</b>: upsert + đọc threshold config.</description></item>
/// </list>
/// </remarks>
[ApiController]
[Route("api/ambient")]
[Produces("application/json")]
public class AmbientReadingsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AmbientReadingsController(IMediator mediator) { _mediator = mediator; }

    /// <summary>
    /// Batch ingest ambient readings (tối đa 100 reading mỗi request).
    /// </summary>
    /// <remarks>
    /// Endpoint dành cho IoT edge device hoặc <c>WeatherSyncBackgroundService</c> đẩy batch lên.
    ///
    /// Cách hoạt động:
    /// - Authorize bằng <c>ApiKey</c> scheme + policy <c>EnvironmentalIngest</c>.
    /// - Validate <c>Readings.Count &lt;= 100</c>; mỗi record cần <c>SiteId</c>, <c>Time</c> (UTC), <c>AmbientTemperature</c>.
    /// - Insert vào TimescaleDB hypertable; trùng <c>(SiteId, Time)</c> sẽ bị skip (idempotent).
    /// </remarks>
    /// <param name="cmd">Batch readings.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="201">Ingest thành công, trả số reading đã insert/skip.</response>
    /// <response code="400">Batch vượt 100 record hoặc thiếu field bắt buộc.</response>
    /// <response code="401">Thiếu ApiKey.</response>
    /// <response code="403">ApiKey không có scope <c>EnvironmentalIngest</c>.</response>
    [HttpPost("readings/batch")]
    [Authorize(AuthenticationSchemes = "ApiKey", Policy = "EnvironmentalIngest")]
    public async Task<IActionResult> BatchIngest([FromBody] BatchIngestAmbientReadingsCommand cmd, CancellationToken ct)
    {
        var result = await _mediator.Send(cmd, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Lịch sử ambient readings theo site (cursor-based pagination).
    /// </summary>
    /// <remarks>
    /// Query parameters:
    /// <list type="bullet">
    ///   <item><description><c>SiteId</c>: bắt buộc.</description></item>
    ///   <item><description><c>From</c>, <c>To</c>: UTC range filter (tùy chọn).</description></item>
    ///   <item><description><c>Cursor</c>: timestamp của record cuối trang trước.</description></item>
    ///   <item><description><c>Limit</c>: mặc định 100, tối đa 1000.</description></item>
    /// </list>
    /// Trả kèm <c>nextCursor</c> + <c>hasMore</c>; không trả <c>totalCount</c> (TimescaleDB tránh full scan).
    /// </remarks>
    /// <param name="query">Filter + cursor + limit.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Trả danh sách readings.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    [HttpGet("readings/history")]
    [Authorize(Roles = "Admin,Manager,Staff,Customer")]
    public async Task<IActionResult> GetHistory([FromQuery] GetAmbientReadingHistoryQuery query, CancellationToken ct)
    {
        var result = await _mediator.Send(query, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Reading ambient mới nhất của một site.
    /// </summary>
    /// <remarks>
    /// Dùng cho dashboard widget. Trả 1 record duy nhất — record có <c>Time</c> lớn nhất theo <c>SiteId</c>.
    /// </remarks>
    /// <param name="siteId">Id của site.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Trả reading mới nhất.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="404">Site chưa có data.</response>
    [HttpGet("readings/latest")]
    [Authorize(Roles = "Admin,Manager,Staff,Customer")]
    public async Task<IActionResult> GetLatest([FromQuery] Guid siteId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetLatestAmbientReadingQuery { SiteId = siteId }, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Upsert <see cref="BatteryService.Domain.Entities.AmbientThresholdConfig"/> cho 1 site.
    /// </summary>
    /// <remarks>
    /// Mỗi site có duy nhất 1 config active. Nếu site đã có config → cập nhật; chưa có → tạo mới.
    ///
    /// Field hỗ trợ:
    /// <list type="bullet">
    ///   <item><description><c>HighAmbientTempWarning</c> / <c>HighAmbientTempCritical</c>: ngưỡng cảnh báo nhiệt độ môi trường.</description></item>
    ///   <item><description><c>HighHumidityWarning</c> / <c>HighHumidityCritical</c>: ngưỡng độ ẩm.</description></item>
    ///   <item><description><c>ComboTempThreshold</c> + <c>ComboHumidityThreshold</c>: combo rule — temp ≥ X AND humidity ≥ Y → <c>HighTempHumidityCombo</c> anomaly.</description></item>
    ///   <item><description><c>Enabled</c>: tắt nhanh threshold mà không xóa config.</description></item>
    /// </list>
    /// </remarks>
    /// <param name="cmd">Threshold config payload.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Upsert thành công.</response>
    /// <response code="400">Site không tồn tại hoặc threshold không hợp lệ (warning &gt; critical).</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin/Manager.</response>
    [HttpPut("threshold-configs")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> UpsertThreshold([FromBody] UpsertAmbientThresholdConfigCommand cmd, CancellationToken ct)
    {
        var result = await _mediator.Send(cmd, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Lấy threshold config theo site.
    /// </summary>
    /// <param name="siteId">Id của site.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Trả config.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin/Manager.</response>
    /// <response code="404">Site chưa được cấu hình threshold.</response>
    [HttpGet("threshold-configs/by-site/{siteId:guid}")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> GetThresholdBySite(Guid siteId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetAmbientThresholdBySiteQuery { SiteId = siteId }, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Liệt kê toàn bộ threshold config (có phân trang + filter).
    /// </summary>
    /// <remarks>
    /// Dùng cho trang Admin xem nhanh tất cả site đã cấu hình ngưỡng.
    /// Filter <c>!IsDeleted</c>. Sort mặc định theo <c>UpdatedAt DESC</c>.
    /// </remarks>
    /// <param name="query">Phân trang + filter.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Trả danh sách config.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin/Manager.</response>
    [HttpGet("threshold-configs")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> ListThresholds([FromQuery] ListAmbientThresholdsQuery query, CancellationToken ct)
    {
        var result = await _mediator.Send(query, ct);
        return StatusCode(result.StatusCode, result);
    }
}
