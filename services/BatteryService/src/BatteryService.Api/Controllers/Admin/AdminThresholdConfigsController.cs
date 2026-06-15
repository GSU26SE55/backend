using BatteryService.Application.CQRS.Command.ThresholdConfig;
using BatteryService.Application.DTOs;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedContracts.Common.Responses;

namespace BatteryService.Api.Controllers.Admin;

/// <summary>
/// Module admin quản lý <b>ThresholdConfig</b>: upsert cấu hình ngưỡng cảnh báo theo BatteryType.
/// Toàn bộ endpoint trong controller này chỉ dành cho Admin.
/// </summary>
[ApiController]
[Route("api/admin/thresholds")]
[Produces("application/json")]
[ApiExplorerSettings(GroupName = "admin")]
[Authorize]
public class AdminThresholdConfigsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AdminThresholdConfigsController(IMediator mediator)
    {
        _mediator = mediator;
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
