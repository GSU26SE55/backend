using BatteryService.Api.Authentication;
using BatteryService.Application.CQRS.Command.SensorReading;
using BatteryService.Application.CQRS.Query.SensorReading;
using BatteryService.Application.DTOs;
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
    /// Ingest hàng loạt SensorReading (dành cho IoT gateway).
    /// </summary>
    /// <remarks>
    /// Authentication:
    /// - Yêu cầu header <c>X-Api-Key: {key}</c> khớp với cấu hình <c>ApiKeys:SensorIngest</c>.
    /// - <b>Không</b> cần JWT.
    ///
    /// Body request:
    /// - <c>Items</c>: list các <see cref="SensorReadingItem"/>, bắt buộc có ít nhất 1 phần tử.
    ///   Mỗi item:
    ///   - <c>BatteryAssetId</c>: bắt buộc, GUID của asset đang đo.
    ///   - <c>Time</c>: bắt buộc, thời điểm đo. Không được ở tương lai quá 5 phút. Hệ thống tự convert sang UTC.
    ///   - <c>Voltage</c>: ≥ 0 (V).
    ///   - <c>Current</c>: số thực, có thể âm (đang xả) hoặc dương (đang sạc) - tùy convention.
    ///   - <c>Temperature</c>: [-50, 120] (°C).
    ///   - <c>SocPercent</c>: [0, 100] (%).
    ///   - <c>CycleCount</c>: tùy chọn, ≥ 0.
    ///   - <c>SourceDeviceId</c>: tùy chọn, ≤ 64 ký tự (định danh thiết bị đo).
    ///
    /// Cách hoạt động:
    /// - Validate toàn bộ items (gom hết lỗi → 400).
    /// - Lọc các <c>BatteryAssetId</c> hợp lệ, query 1 lần để check tồn tại + <c>!IsDeleted</c> → dictionary.
    /// - Với mỗi item:
    ///   - Nếu asset không tồn tại trong dictionary → <b>skip</b> (không throw, được tính vào <c>Skipped</c>).
    ///   - Convert <c>Time</c> sang UTC.
    ///   - Insert SensorReading mới.
    ///   - Cập nhật <c>asset.LastSensorReadingAt</c> nếu reading mới hơn timestamp đang lưu.
    /// - Một <c>SaveChangesAsync</c> cho cả batch để tối ưu.
    /// - Trả <see cref="SensorReadingBatchIngestResult"/> với count <c>TotalReceived</c>, <c>Inserted</c>, <c>Skipped</c>.
    ///
    /// Lưu ý:
    /// - Endpoint <b>không</b> throw khi gặp asset không tồn tại; gateway có thể tiếp tục gửi data các asset khác.
    ///   Trường <c>Skipped</c> trong response giúp gateway phát hiện sai cấu hình mapping.
    /// - Duplicate (BatteryAssetId, Time) sẽ raise unique constraint của hypertable → throw 500. Gateway cần đảm bảo Time đủ resolution.
    /// - Sprint 3: AnomalyDetector sẽ quét reading mới và phát sinh Alert dựa trên ThresholdConfig.
    /// </remarks>
    /// <param name="command">Batch sensor readings.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa <see cref="SensorReadingBatchIngestResult"/>.</returns>
    /// <response code="200">Batch xử lý thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ (xem <c>ListErrors</c>).</response>
    /// <response code="401">Thiếu hoặc sai <c>X-Api-Key</c>.</response>
    [HttpPost("batch")]
    [Authorize(AuthenticationSchemes = ApiKeyAuthenticationHandler.SchemeName)]
    [ProducesResponseType(typeof(CommonResponse<SensorReadingBatchIngestResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<SensorReadingBatchIngestResult>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> BatchIngest([FromBody] BatchIngestSensorReadingsCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Lấy lịch sử SensorReading của một BatteryAsset trong khoảng thời gian.
    /// </summary>
    /// <remarks>
    /// Query parameters:
    /// - <c>BatteryAssetId</c>: bắt buộc, asset cần xem history.
    /// - <c>From</c>: tùy chọn, UTC, lọc <c>Time &gt;= From</c>.
    /// - <c>To</c>: tùy chọn, UTC, lọc <c>Time &lt;= To</c>.
    /// - <c>PageNumber</c>, <c>PageSize</c>: phân trang (max page size 100).
    ///
    /// Cách hoạt động:
    /// - Filter theo asset + time range.
    /// - Sort <c>Time</c> giảm dần (đo mới nhất lên đầu).
    /// - Projection thẳng sang <see cref="SensorReadingDto"/>.
    /// - Tận dụng TimescaleDB chunk pruning để query time range nhanh.
    ///
    /// Use case:
    /// - Mobile/Web vẽ biểu đồ voltage/temperature theo thời gian.
    /// - Manager phân tích root cause khi có alert.
    ///
    /// Lưu ý:
    /// - Endpoint <b>chưa</b> enforce server-side rằng Customer chỉ xem được history của asset mình sở hữu - phải kiểm tra ở FE/Mobile hoặc bổ sung trong sprint sau.
    /// - Với time range lớn (ví dụ 1 năm) và pin có tần số đo cao (mỗi 30s), TotalItems có thể rất lớn; nên giới hạn time range hợp lý hoặc dùng aggregation (sẽ làm Sprint 7).
    /// </remarks>
    /// <param name="query">Filter time range + phân trang.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa <see cref="PaginationResponse{T}"/> các <see cref="SensorReadingDto"/>.</returns>
    /// <response code="200">Trả history.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role phù hợp.</response>
    [HttpGet]
    [Authorize(Roles = "Admin,Manager,Staff,Customer")]
    [ProducesResponseType(typeof(CommonResponse<PaginationResponse<SensorReadingDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetHistory([FromQuery] GetSensorReadingHistoryQuery query, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(query, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Lấy SensorReading mới nhất của một BatteryAsset.
    /// </summary>
    /// <remarks>
    /// Query parameter:
    /// - <c>assetId</c>: bắt buộc, Id BatteryAsset.
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
    /// <param name="assetId">Id BatteryAsset.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa <see cref="SensorReadingDto"/>.</returns>
    /// <response code="200">Trả reading mới nhất.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role phù hợp.</response>
    /// <response code="404">Asset chưa có reading nào.</response>
    [HttpGet("latest")]
    [Authorize(Roles = "Admin,Manager,Staff,Customer")]
    [ProducesResponseType(typeof(CommonResponse<SensorReadingDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<SensorReadingDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLatest([FromQuery] Guid assetId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetLatestSensorReadingQuery { BatteryAssetId = assetId }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}
