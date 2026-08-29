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
///   <item><description><b>Admin/Manager/Staff</b>: đọc (list, get by type) - Staff cần ngưỡng để tô vùng cảnh báo trên chart telemetry.</description></item>
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
    /// <response code="403">Không có role Admin/Manager/Staff.</response>
    [HttpGet]
    [Authorize(Roles = "Admin,Manager,Staff")]
    [ProducesResponseType(typeof(CommonResponse<PaginationResponse<ThresholdConfigDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAll([FromQuery] GetThresholdConfigsQuery query, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(query, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Lấy ThresholdConfig hiện hành của 1 BatteryType (voltage/temp/SOC thresholds) — sensor reading được so với config này để trigger Alert.
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
    /// - Chưa cấu hình → <b>200</b> với <c>data = null</c> (không phải 404): đây là query thành công
    ///   trả về tập rỗng. Client phân biệt "chưa cấu hình" (null) với lỗi thật (403/500).
    ///
    /// Use case:
    /// - AnomalyDetector tải threshold trước khi đánh giá reading.
    /// - Admin xem config hiện tại của loại pin để quyết định có cần đổi không.
    /// </remarks>
    /// <param name="batteryTypeId">Id BatteryType.</param>
    /// <param name="includeInactive">Có bao gồm config inactive không.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa <see cref="ThresholdConfigDto"/>.</returns>
    /// <response code="200">Trả về config, hoặc <c>data = null</c> nếu BatteryType chưa có config nào.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin/Manager/Staff.</response>
    [HttpGet("by-type/{batteryTypeId:guid}")]
    [Authorize(Roles = "Admin,Manager,Staff")]
    [ProducesResponseType(typeof(CommonResponse<ThresholdConfigDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetByBatteryType(Guid batteryTypeId, [FromQuery] bool includeInactive, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetThresholdConfigByBatteryTypeQuery
        {
            BatteryTypeId = batteryTypeId,
            IncludeInactive = includeInactive
        }, cancellationToken);

        return StatusCode(result.StatusCode, result);
    }

}
