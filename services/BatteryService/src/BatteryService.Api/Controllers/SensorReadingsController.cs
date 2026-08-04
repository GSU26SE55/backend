using BatteryService.Api.Authentication;
using BatteryService.Application.CQRS.Command.SensorReading;
using BatteryService.Application.CQRS.Query.SensorReading;
using BatteryService.Application.DTOs;
using BatteryService.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedContracts.Common.Responses;

namespace BatteryService.Api.Controllers;

/// <summary>
/// Nhóm endpoint xử lý <b>SensorReading</b> - bản ghi đo lường thời gian thực từ pin (voltage, current, temperature, SOC...).
/// Bảng <c>sensor_readings</c> là TimescaleDB hypertable, tự động partition theo thời gian.
/// </summary>
/// <remarks>
/// Đặc thù endpoint:
/// <list type="bullet">
///   <item><description><b>POST /batch</b>: dùng <b>ApiKey authentication</b>, không phải JWT. Dành cho IoT gateway / sensor hub đẩy data vào.</description></item>
///   <item><description><b>GET</b>: dùng JWT đăng nhập như các endpoint khác.</description></item>
/// </list>
///
/// Lý do dùng ApiKey thay vì JWT cho ingest:
/// - IoT gateway thường không có khả năng refresh token / xử lý OAuth flow.
/// - ApiKey cố định (cấu hình qua <c>ApiKeys:SensorIngest</c>) đơn giản, có thể rotate khi cần.
/// - Tách kênh ingest khỏi kênh user/web giúp dễ áp rate limit và monitoring riêng.
/// </remarks>
[ApiController]
[Route("api/sensor-readings")]
[Produces("application/json")]
public class SensorReadingsController : ControllerBase
{
    private readonly IMediator _mediator;

    public SensorReadingsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Ingest hàng loạt SensorReading từ IoT gateway / ESP32 / simulator.
    /// </summary>
    /// <remarks>
    /// Authentication (Sprint IoT-1 #243/#246):
    /// <list type="bullet">
    ///   <item><description><b>Per-device key</b> (production): <c>X-Api-Key: iotk_...</c> + <c>X-Device-Code: ESP32-001</c>. Key phải có scope <c>SensorIngest</c>.</description></item>
    ///   <item><description><b>Legacy global key</b> (simulator/MVP, sẽ deprecate): <c>X-Api-Key: {ApiKeys:SensorIngest}</c>. Không có claim device → không apply calibration.</description></item>
    ///   <item><description><b>Không</b> cần JWT.</description></item>
    /// </list>
    ///
    /// Optional headers:
    /// <list type="bullet">
    ///   <item><description><c>X-Device-Code</c>: backend cross-check với device gắn key. Mismatch sẽ bị reject ở auth handler.</description></item>
    ///   <item><description><c>Idempotency-Key</c>: UUID. Device gửi lại cùng key khi retry sau timeout — Sprint IoT-2 sẽ dedup; hiện tại lưu vào command để inbox idempotency middleware xử lý.</description></item>
    /// </list>
    ///
    /// Body request:
    /// <list type="bullet">
    ///   <item><description><c>Items</c>: list <see cref="SensorReadingItem"/>, &gt;= 1 và &lt;= 1000 phần tử.</description></item>
    /// </list>
    /// Mỗi item:
    /// <list type="bullet">
    ///   <item><description><c>BatteryAssetId</c> HOẶC <c>BatteryAssetSerial</c>: bắt buộc 1 trong 2. Serial được resolve về Id ở backend (so khớp <c>SerialNumber.ToUpperInvariant()</c>).</description></item>
    ///   <item><description><c>Time</c>: bắt buộc, UTC, không được tương lai quá 5 phút.</description></item>
    ///   <item><description><c>DeviceTimestamp</c>: tùy chọn — timestamp ghi nhận tại device. Backend check skew vs <c>UtcNow</c>; lệch &gt; 5 phút → 400 (field validation).</description></item>
    ///   <item><description><c>Voltage</c>: ≥ 0 (V). Outlier check sau calibration: bị loại nếu &gt; 100V.</description></item>
    ///   <item><description><c>Current</c>: số thực (± sạc/xả). Outlier: <c>|current| &gt; 1000A</c>.</description></item>
    ///   <item><description><c>Temperature</c>: [-50, 120] field-validate; outlier check sau calibration cũng [-50, 120].</description></item>
    ///   <item><description><c>SocPercent</c>: [0, 100].</description></item>
    ///   <item><description><c>CycleCount</c>: tùy chọn, ≥ 0.</description></item>
    ///   <item><description><c>SourceDeviceId</c>: tùy chọn, ≤ 64 ký tự — định danh tự do từ device.</description></item>
    ///   <item><description><c>SourceType</c>: <see cref="SensorReadingSourceTypeEnum"/> — <c>Bms=1, IotGateway=2, External=3</c>. Default <c>IotGateway</c>.</description></item>
    ///   <item><description><c>BmsErrorCode</c> ≤ 64, <c>SensorSourceCode</c> ≤ 20 (§52.9 multi-sensor cùng pin).</description></item>
    ///   <item><description>Tier 2 metrics: <c>InternalResistanceMilliohm</c> (&gt; 0), <c>CellVoltageDeltaMv</c> (≥ 0).</description></item>
    /// </list>
    ///
    /// Cách hoạt động (Sprint IoT-1):
    /// <list type="number">
    ///   <item><description>Validate field-level → 400 + <c>ListErrors</c> nếu lỗi.</description></item>
    ///   <item><description>Pull <c>X-Device-Code</c> + <c>Idempotency-Key</c> từ header; pull <c>AuthenticatedDeviceId</c> từ claim API key.</description></item>
    ///   <item><description>Resolve <c>BatteryAssetSerial</c> → Id (1 batch query).</description></item>
    ///   <item><description>Query asset hợp lệ (<c>!IsDeleted</c>) → dictionary.</description></item>
    ///   <item><description>Load <c>IotDeviceCalibration</c> của device hiện tại (nếu auth per-device) — chỉ những calibration chưa expire.</description></item>
    ///   <item><description>Với mỗi item: skip nếu asset không có → tăng <c>Skipped</c>. Apply calibration <c>raw * Scale + Offset</c>. Reject outlier (voltage/current/temperature ngoài bound) → tăng <c>Skipped</c>.</description></item>
    ///   <item><description>Insert SensorReading + update <c>asset.LastSensorReadingAt</c>.</description></item>
    ///   <item><description>Nếu có device đang push: update <c>IotDevice.LastSeenAt</c>, flip <c>Status</c> Offline/Pending → Active.</description></item>
    ///   <item><description>1 SaveChanges cho cả batch.</description></item>
    /// </list>
    ///
    /// Lưu ý:
    /// <list type="bullet">
    ///   <item><description>Endpoint <b>không throw</b> khi asset không tồn tại; gateway tiếp tục gửi data asset khác. <c>Skipped</c> trong response giúp gateway phát hiện sai mapping.</description></item>
    ///   <item><description>Duplicate <c>(BatteryAssetId, Time)</c> → unique constraint của hypertable raise 500. Device cần đảm bảo Time đủ resolution (ms) hoặc dùng <c>SensorSourceCode</c> khác.</description></item>
    ///   <item><description>Outlier bị loại <b>không</b> raise exception — chỉ count vào <c>Skipped</c> + ghi log warning. Calibration sai nghiêm trọng sẽ thấy <c>Skipped</c> tăng đột biến trên dashboard.</description></item>
    ///   <item><description>AnomalyDetector chạy background quét reading mới → phát sinh Alert dựa trên ThresholdConfig.</description></item>
    ///   <item><description>Field <c>DeviceCode</c>, <c>IdempotencyKey</c>, <c>AuthenticatedDeviceId</c> trong command có <c>[JsonIgnore][BindNever]</c> — client không thể override qua body.</description></item>
    /// </list>
    /// </remarks>
    /// <param name="command">Batch sensor readings.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa <see cref="SensorReadingBatchIngestResult"/>.</returns>
    /// <response code="201">Batch tạo readings thành công (xem <c>Inserted</c> / <c>Skipped</c> / <c>TotalReceived</c>). Sensor readings là resource mới được persist vào hypertable.</response>
    /// <response code="400">Dữ liệu không hợp lệ (xem <c>ListErrors</c> — field-level).</response>
    /// <response code="401">Thiếu / sai <c>X-Api-Key</c>, hoặc API key thiếu scope <c>SensorIngest</c>.</response>
    [HttpPost("batch")]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(AuthenticationSchemes = ApiKeyAuthenticationHandler.SchemeName)]
    [IotApiKeyScopeRequirement(IotApiKeyScopeEnum.SensorIngest)]
    [ProducesResponseType(typeof(CommonResponse<SensorReadingBatchIngestResult>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(CommonResponse<SensorReadingBatchIngestResult>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> BatchIngest([FromBody] BatchIngestSensorReadingsCommand command, CancellationToken cancellationToken)
    {
        // Sprint IoT-1 (#246) — pull headers.
        if (Request.Headers.TryGetValue(ApiKeyAuthenticationHandler.DeviceCodeHeader, out var dc))
            command.DeviceCode = dc.FirstOrDefault();
        if (Request.Headers.TryGetValue("Idempotency-Key", out var ik))
            command.IdempotencyKey = ik.FirstOrDefault();

        // Map per-device claim → AuthenticatedDeviceId.
        var idClaim = User.FindFirst(ApiKeyAuthenticationHandler.ClaimDeviceId)?.Value;
        if (!string.IsNullOrEmpty(idClaim) && Guid.TryParse(idClaim, out var did))
            command.AuthenticatedDeviceId = did;

        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Lấy lịch sử SensorReading của một BatteryAsset trong khoảng thời gian.
    /// </summary>
    /// <remarks>
    /// Query parameters:
    /// - <c>batteryAssetId</c>: bắt buộc trên route, asset cần xem history.
    /// - <c>From</c>: tùy chọn, UTC, lọc <c>Time &gt;= From</c>.
    /// - <c>To</c>: tùy chọn, UTC, lọc <c>Time &lt;= To</c>.
    /// - <c>Limit</c>: số record mỗi trang, mặc định 100, tối đa 1000.
    /// - <c>Cursor</c>: timestamp của record cuối trang trước; dùng để lấy trang tiếp theo.
    ///
    /// Cách hoạt động:
    /// - Filter theo asset + time range.
    /// - Sort <c>Time</c> giảm dần (đo mới nhất lên đầu).
    /// - Nếu có <c>Cursor</c>, chỉ lấy record có <c>Time &lt; Cursor</c>.
    /// - Projection thẳng sang <see cref="SensorReadingDto"/>.
    /// - Tận dụng TimescaleDB chunk pruning để query time range nhanh.
    ///
    /// Use case:
    /// - Mobile/Web vẽ biểu đồ voltage/temperature theo thời gian.
    /// - Manager phân tích root cause khi có alert.
    ///
    /// Lưu ý:
    /// - Endpoint <b>chưa</b> enforce server-side rằng Customer chỉ xem được history của asset mình sở hữu - phải kiểm tra ở FE/Mobile hoặc bổ sung trong sprint sau.
    /// - Không trả <c>TotalItems</c> cho time-series vì count full range rất tốn kém. FE dùng <c>HasMore</c> và <c>NextCursor</c>.
    /// - Với time range lớn (ví dụ 1 năm) và pin có tần số đo cao (mỗi 30s), nên dùng aggregation (sẽ làm Sprint 7) thay vì raw history.
    /// </remarks>
    /// <param name="batteryAssetId">Id BatteryAsset.</param>
    /// <param name="query">Filter time range + cursor paging.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa items + next cursor.</returns>
    /// <response code="200">Trả history.</response>
    /// <response code="400">Query không hợp lệ.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role phù hợp.</response>
    [HttpGet("{batteryAssetId:guid}/history")]
    [Authorize(Roles = "Admin,Manager,Staff,Customer")]
    [ProducesResponseType(typeof(CommonResponse<SensorReadingHistoryResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<SensorReadingHistoryResponseDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetHistory(Guid batteryAssetId, [FromQuery] GetSensorReadingHistoryQuery query, CancellationToken cancellationToken)
    {
        query.BatteryAssetId = batteryAssetId;
        var result = await _mediator.Send(query, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Lấy dữ liệu SensorReading đã được gộp theo khoảng thời gian (aggregate) cho chart.
    /// </summary>
    /// <remarks>
    /// Query parameters:
    /// - <c>batteryAssetId</c>: bắt buộc trên route.
    /// - <c>From</c>: tùy chọn, UTC, lọc <c>Time &gt;= From</c>.
    /// - <c>To</c>: tùy chọn, UTC, lọc <c>Time &lt;= To</c>.
    /// - <c>Interval</c>: khoảng bucket — <c>1m</c>, <c>5m</c>, <c>15m</c>, <c>1h</c>, <c>1d</c>. Mặc định <c>1h</c>.
    ///
    /// Cách hoạt động:
    /// - Filter theo asset + time range.
    /// - Gộp readings theo <c>Interval</c>, tính AVG cho từng metric (Voltage, Current, Temperature, SocPercent, SohPercent).
    /// - Trả danh sách bucket sắp xếp tăng dần theo thời gian.
    ///
    /// Use case:
    /// - FE/Mobile vẽ biểu đồ SOC/Voltage/Temperature theo thời gian.
    /// - Thay thế <c>/history</c> khi time range lớn (> 1 ngày) để tránh quá nhiều data points.
    ///
    /// Lưu ý:
    /// - <c>AvgSohPercent</c> là nullable; trả <c>null</c> nếu không có reading nào có SohPercent trong bucket.
    /// - Không trả totalItems — FE dùng độ dài mảng <c>items</c>.
    /// </remarks>
    /// <param name="batteryAssetId">Id BatteryAsset.</param>
    /// <param name="query">Filter + interval.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa danh sách <see cref="SensorReadingAggregateDto"/>.</returns>
    /// <response code="200">Trả aggregate data.</response>
    /// <response code="400">Query không hợp lệ (interval không đúng, time range ngược).</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role phù hợp.</response>
    [HttpGet("{batteryAssetId:guid}/aggregate")]
    [Authorize(Roles = "Admin,Manager,Staff,Customer")]
    [ProducesResponseType(typeof(CommonResponse<List<SensorReadingAggregateDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<List<SensorReadingAggregateDto>>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAggregate(Guid batteryAssetId, [FromQuery] GetSensorReadingAggregateQuery query, CancellationToken cancellationToken)
    {
        query.BatteryAssetId = batteryAssetId;
        var result = await _mediator.Send(query, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Sprint Bonus NS-06 (#650, PA-4) — aggregate cố định bucket <b>1 giờ</b> min/max dòng nạp/xả (+ V/T + avg) của 1 pin, đọc từ TimescaleDB continuous aggregate; dùng cho chart range dài (tháng/năm).
    /// </summary>
    /// <remarks>
    /// Đọc từ materialized view <c>sensor_readings_agg_1h</c> (TimescaleDB continuous aggregate, tự refresh mỗi phút, bật real-time aggregation nên bao gồm cả dữ liệu gần đây chưa materialize).
    ///
    /// Query parameters:
    /// - <c>batteryAssetId</c>: bắt buộc trên route.
    /// - <c>From</c>: tùy chọn, UTC, lọc <c>bucket &gt;= From</c>.
    /// - <c>To</c>: tùy chọn, UTC, lọc <c>bucket &lt;= To</c>.
    /// - KHÔNG có <c>Interval</c> — cố định 1h.
    ///
    /// Cách hoạt động:
    /// - Chỉ tính trên reading source <c>primary</c> (bỏ redundant/external-temp — tránh đếm 3 lần).
    /// - Mỗi bucket 1h: AVG các metric + MIN/MAX Voltage/Temperature + MIN/MAX/AVG dòng tách 2 chiều nạp (current &gt; 0) / xả (current &lt; 0, trả trị tuyệt đối dương) + <c>chargeSampleCount</c>/<c>dischargeSampleCount</c>.
    /// - Trả danh sách bucket sắp xếp tăng dần theo thời gian.
    ///
    /// Use case:
    /// - FE/Mobile vẽ chart min/max nạp/xả range dài mà <c>/aggregate</c> (in-memory, bounded ~7 ngày) sẽ chậm/hết RAM.
    /// - Range ngắn hoặc cần interval linh hoạt (1m/5m/15m/1d) → dùng <c>/aggregate</c> thay endpoint này.
    ///
    /// Lưu ý:
    /// - Các field min/max nạp/xả (<c>maxChargeCurrent</c>, <c>minDischargeCurrent</c>, …) và min/max V/T là <c>nullable</c>: trả <c>null</c> nếu bucket không có mẫu chiều đó. LUÔN trả giá trị dương cho cả 2 chiều. KHÔNG trả <c>0</c> (0A là giá trị đo hợp lệ ≠ không có dữ liệu).
    /// - <c>batteryAssetId</c> lấy từ route (không bind qua query/body — chống client override).
    /// </remarks>
    /// <param name="batteryAssetId">Id BatteryAsset (route).</param>
    /// <param name="query">Filter thời gian (<c>From</c>/<c>To</c>, UTC).</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa danh sách <see cref="SensorReadingAggregateDto"/> (mỗi phần tử là 1 bucket 1h).</returns>
    /// <response code="200">Trả aggregate 1h data (mảng bucket tăng dần theo thời gian).</response>
    /// <response code="400">Query không hợp lệ — <c>batteryAssetId</c> rỗng (field-level <c>listErrors</c>).</response>
    /// <response code="422">Cross-field: <c>From &gt; To</c> (field-level <c>listErrors</c>).</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin/Manager/Staff/Customer.</response>
    [HttpGet("{batteryAssetId:guid}/aggregate/hourly")]
    [Authorize(Roles = "Admin,Manager,Staff,Customer")]
    [ProducesResponseType(typeof(CommonResponse<List<SensorReadingAggregateDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<List<SensorReadingAggregateDto>>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(CommonResponse<List<SensorReadingAggregateDto>>), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetHourlyAggregate(Guid batteryAssetId, [FromQuery] GetSensorReadingHourlyAggregateQuery query, CancellationToken cancellationToken)
    {
        query.BatteryAssetId = batteryAssetId;
        var result = await _mediator.Send(query, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Lấy SensorReading mới nhất của 1 BatteryAsset (snapshot tức thời) — dùng cho Customer mobile app widget hiển thị voltage/current/SOC real-time; TimescaleDB index hỗ trợ.
    /// </summary>
    /// <remarks>
    /// Route parameter:
    /// - <c>batteryAssetId</c>: bắt buộc, Id BatteryAsset.
    ///
    /// Cách hoạt động:
    /// - Filter <c>BatteryAssetId = assetId</c>, <c>OrderByDescending(Time).FirstOrDefault()</c>.
    /// - TimescaleDB tối ưu việc tìm reading mới nhất trên hypertable.
    /// - 404 nếu asset chưa có reading nào (chú ý: không 404 cho asset không tồn tại - vẫn trả 404 do query không match).
    ///
    /// Tips:
    /// - Nếu chỉ cần snapshot tổng quan (reading + alert count), dùng <c>GET /api/battery-assets/{id}/realtime</c> sẽ thuận tiện hơn.
    /// - Endpoint này phù hợp khi chỉ muốn raw reading, không cần asset metadata.
    /// </remarks>
    /// <param name="batteryAssetId">Id BatteryAsset.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa <see cref="SensorReadingDto"/>.</returns>
    /// <response code="200">Trả reading mới nhất.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role phù hợp.</response>
    /// <response code="404">Asset chưa có reading nào.</response>
    [HttpGet("{batteryAssetId:guid}/latest")]
    [Authorize(Roles = "Admin,Manager,Staff,Customer")]
    [ProducesResponseType(typeof(CommonResponse<SensorReadingDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<SensorReadingDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLatest(Guid batteryAssetId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetLatestSensorReadingQuery { BatteryAssetId = batteryAssetId }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}
