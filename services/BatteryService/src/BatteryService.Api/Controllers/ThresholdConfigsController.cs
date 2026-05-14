using BatteryService.Application.CQRS.Command.ThresholdConfig;
using BatteryService.Application.CQRS.Query.ThresholdConfig;
using BatteryService.Application.DTOs;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedContracts.Common.Responses;

namespace BatteryService.Api.Controllers;

/// <summary>
/// Nhóm endpoint quản lý <b>ThresholdConfig</b> - cấu hình ngưỡng cảnh báo cho từng BatteryType.
/// </summary>
/// <remarks>
/// Mỗi BatteryType có 1 ThresholdConfig active duy nhất tại mỗi thời điểm; <c>AnomalyDetector</c> ở Sprint 3 sẽ dùng
/// config này để so với SensorReading và phát sinh Alert.
///
/// Cấu trúc ngưỡng:
/// <list type="bullet">
///   <item><description><b>Voltage</b>: <c>VoltageMin</c>, <c>VoltageMax</c> (V) - vượt khoảng này → anomaly Overvoltage/Undervoltage.</description></item>
///   <item><description><b>Temperature</b>: <c>TemperatureMin</c>, <c>TemperatureMax</c> (°C) - vượt → Overheat.</description></item>
///   <item><description><b>SOC</b>: <c>SocWarningThreshold</c> &gt; <c>SocCriticalThreshold</c> (%) - SOC dưới warning → LowSoc warning; dưới critical → LowSoc critical.</description></item>
///   <item><description><b>Current</b>: <c>CurrentMaxCharge</c>, <c>CurrentMaxDischarge</c> (A, tùy chọn) - vượt → AbnormalCharging / RapidDischarge.</description></item>
/// </list>
///
/// Phân quyền:
/// <list type="bullet">
///   <item><description><b>Admin/Manager</b>: đọc (list, get by type).</description></item>
///   <item><description><b>Admin</b>: cập nhật (Upsert).</description></item>
/// </list>
/// </remarks>
[ApiController]
[Route("api/thresholds")]
[Produces("application/json")]
public class ThresholdConfigsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ThresholdConfigsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Lấy danh sách ThresholdConfig có phân trang + filter.
    /// </summary>
    /// <remarks>
    /// Query parameters:
    /// - <c>PageNumber</c>, <c>PageSize</c>: phân trang.
    /// - <c>BatteryTypeId</c>: tùy chọn, lọc theo loại pin.
    /// - <c>IsActive</c>: tùy chọn, mặc định <c>true</c> trong query DTO. Truyền <c>false</c> để xem các config cũ đã bị deactivate.
    ///
    /// Cách hoạt động:
    /// - Filter <c>!IsDeleted</c>.
    /// - Sort theo <c>EffectiveFromUtc</c> giảm dần (config mới hiệu lực nhất lên đầu).
    /// </remarks>
    /// <param name="query">Filter + phân trang.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa <see cref="PaginationResponse{T}"/> các <see cref="ThresholdConfigDto"/>.</returns>
    /// <response code="200">Trả danh sách.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin/Manager.</response>
    [HttpGet]
    [Authorize(Roles = "Admin,Manager")]
    [ProducesResponseType(typeof(CommonResponse<PaginationResponse<ThresholdConfigDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAll([FromQuery] GetThresholdConfigsQuery query, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(query, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Lấy ThresholdConfig hiện tại của một BatteryType.
    /// </summary>
    /// <remarks>
    /// Trả 1 config duy nhất - cái có <c>EffectiveFromUtc</c> mới nhất.
    ///
    /// Query parameter:
    /// - <c>includeInactive</c>: mặc định <c>false</c> - chỉ trả config <c>IsActive = true</c>. Truyền <c>true</c> để cho phép trả về cả config inactive (dùng cho audit/history).
    ///
    /// Cách hoạt động:
    /// - Filter theo <c>BatteryTypeId</c> + <c>!IsDeleted</c>.
    /// - Nếu <c>!includeInactive</c>, thêm filter <c>IsActive = true</c>.
    /// - Sort <c>EffectiveFromUtc</c> giảm dần, lấy <c>FirstOrDefault</c>.
    ///
    /// Use case:
    /// - AnomalyDetector tải threshold trước khi đánh giá reading.
    /// - Admin xem config hiện tại của loại pin để quyết định có cần đổi không.
    /// </remarks>
    /// <param name="batteryTypeId">Id BatteryType.</param>
    /// <param name="includeInactive">Có bao gồm config inactive không.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa <see cref="ThresholdConfigDto"/>.</returns>
    /// <response code="200">Trả về config.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin/Manager.</response>
    /// <response code="404">BatteryType chưa có config nào (hoặc đều inactive khi không cho includeInactive).</response>
    [HttpGet("by-type/{batteryTypeId:guid}")]
    [Authorize(Roles = "Admin,Manager")]
    [ProducesResponseType(typeof(CommonResponse<ThresholdConfigDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<ThresholdConfigDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByBatteryType(Guid batteryTypeId, [FromQuery] bool includeInactive, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetThresholdConfigByBatteryTypeQuery
        {
            BatteryTypeId = batteryTypeId,
            IncludeInactive = includeInactive
        }, cancellationToken);

        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Tạo mới hoặc cập nhật (upsert) ThresholdConfig cho một BatteryType.
    /// </summary>
    /// <remarks>
    /// Endpoint semantics: <b>PUT</b> với ngữ nghĩa upsert idempotent. Mỗi BatteryType chỉ có tối đa 1 config active;
    /// gọi endpoint nhiều lần sẽ cập nhật cùng record đó.
    ///
    /// Body request:
    /// - <c>VoltageMin</c>: bắt buộc, &gt; 0 (V).
    /// - <c>VoltageMax</c>: bắt buộc, &gt; VoltageMin.
    /// - <c>TemperatureMin</c>: bắt buộc.
    /// - <c>TemperatureMax</c>: bắt buộc, &gt; TemperatureMin.
    /// - <c>SocWarningThreshold</c>: bắt buộc, [0, 100] (%).
    /// - <c>SocCriticalThreshold</c>: bắt buộc, [0, 100], phải &lt; SocWarningThreshold.
    /// - <c>CurrentMaxCharge</c>: tùy chọn, &gt; 0 (A).
    /// - <c>CurrentMaxDischarge</c>: tùy chọn, &gt; 0 (A).
    /// - <c>EffectiveFromUtc</c>: thời điểm config bắt đầu có hiệu lực. Nếu để mặc định/<c>default</c>, hệ thống dùng <c>DateTime.UtcNow</c>.
    ///
    /// Cách hoạt động:
    /// - Validate đầu vào (gom toàn bộ lỗi → 400).
    /// - Check BatteryType tồn tại + chưa xóa (404).
    /// - Tìm config active hiện tại cho BatteryType:
    ///   - Nếu chưa có: tạo mới với <c>Id = Guid.NewGuid()</c>, <c>IsActive = true</c>, gọi <c>AddAsync</c>.
    ///   - Nếu có: ghi đè các field threshold lên record cũ, <c>UpdateAsync</c>.
    /// - Cập nhật <c>EffectiveFromUtc</c> theo input (hoặc UtcNow).
    /// - <c>IsActive</c> luôn được set thành <c>true</c> sau upsert.
    ///
    /// Lưu ý:
    /// - Endpoint là idempotent về kết quả cuối (cùng input → cùng state) nhưng KHÔNG idempotent về số version trong DB.
    /// - Hiện tại model KHÔNG lưu lịch sử các config cũ (mỗi BatteryType chỉ có 1 row); nếu cần audit history, cần extend model.
    /// </remarks>
    /// <param name="batteryTypeId">Id BatteryType (route).</param>
    /// <param name="command">Thông tin ngưỡng.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa <see cref="ThresholdConfigDto"/> sau upsert.</returns>
    /// <response code="200">Upsert thành công (cả tạo mới và update đều trả 200 ở handler này).</response>
    /// <response code="400">Dữ liệu không hợp lệ.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin.</response>
    /// <response code="404">BatteryType không tồn tại.</response>
    [HttpPut("by-type/{batteryTypeId:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(CommonResponse<ThresholdConfigDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<ThresholdConfigDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<ThresholdConfigDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Upsert(Guid batteryTypeId, [FromBody] UpsertThresholdConfigCommand command, CancellationToken cancellationToken)
    {
        command.BatteryTypeId = batteryTypeId;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}
