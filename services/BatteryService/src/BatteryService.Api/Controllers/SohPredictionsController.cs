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
}
