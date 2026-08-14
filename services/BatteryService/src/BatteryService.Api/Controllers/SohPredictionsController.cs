using BatteryService.Application.CQRS.Query.SohPrediction;
using BatteryService.Application.DTOs;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedContracts.Common.Responses;

namespace BatteryService.Api.Controllers;

/// <summary>
/// BE-AI — endpoint đọc lịch sử SohPrediction (AI populate qua SohPredictionBackgroundService).
/// Dùng cho FE vẽ chart SOH dự đoán theo thời gian trên trang chi tiết pin.
/// </summary>
[ApiController]
[Route("api/v1/soh-predictions")]
[Produces("application/json")]
public class SohPredictionsController : ControllerBase
{
    private readonly IMediator _mediator;

    public SohPredictionsController(IMediator mediator) => _mediator = mediator;

    /// <summary>GET lịch sử SOH prediction của 1 pin (chart dashboard).</summary>
    /// <param name="query">Filter: <c>batteryAssetId</c> (bắt buộc), <c>from/to</c>, phân trang.</param>
    /// <param name="ct">Token hủy request.</param>
    [HttpGet]
    [Authorize(Roles = "Admin,Manager,Staff")]
    [ProducesResponseType(typeof(CommonResponse<PaginationResponse<SohPredictionDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetList([FromQuery] GetSohPredictionsQuery query, CancellationToken ct)
    {
        var result = await _mediator.Send(query, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>SOH từ chuỗi dài (31..4096 timestep) — phân tích lịch sử.</summary>
    /// <remarks>
    /// KHÁC <c>GET /api/v1/soh-predictions</c> ở bản chất: đường này gọi model LONG, không có
    /// MC-dropout (⇒ không confidence/health_stage) và không IsolationForest (⇒ không
    /// anomaly/risk). Phía AI cố ý bỏ anomaly vì IsolationForest được fit trên phân bố
    /// feature của window=30 — chấm chuỗi 4096 bước bằng nó ra số trông hợp lệ mà vô nghĩa.
    /// <para>KHÔNG dùng kết quả này để quyết định tạo ticket. Chỉ để phân tích/vẽ chart.</para>
    /// <para><b>409</b> pin chưa đủ 31 số đo hợp lệ · <b>503</b> AI không phản hồi.</para>
    /// </remarks>
    [HttpGet("long")]
    [Authorize(Roles = "Admin,Manager,Staff")]
    [ProducesResponseType(typeof(CommonResponse<LongSohDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<LongSohDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(CommonResponse<LongSohDto>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(CommonResponse<LongSohDto>), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetLong([FromQuery] GetLongSohQuery query, CancellationToken ct)
    {
        var result = await _mediator.Send(query, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Dự đoán nhiều pin trong MỘT kết nối gRPC — cho màn hình giám sát.</summary>
    /// <remarks>
    /// ⚠️ Đọc <c>isComplete</c> trước khi hiển thị. Bidi stream không có lỗi theo từng
    /// message: một pin có cửa sổ sai làm đứt cả lượt, nên pin KHÔNG có trong <c>items</c>
    /// là pin CHƯA ĐƯỢC CHẤM — hiển thị nó như "không có cảnh báo" là nói sai sự thật.
    /// </remarks>
    [HttpGet("batch")]
    [Authorize(Roles = "Admin,Manager,Staff")]
    [ProducesResponseType(typeof(CommonResponse<BatchPredictionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<BatchPredictionDto>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetBatch([FromQuery] GetBatchPredictionQuery query, CancellationToken ct)
    {
        var result = await _mediator.Send(query, ct);
        return StatusCode(result.StatusCode, result);
    }
}
