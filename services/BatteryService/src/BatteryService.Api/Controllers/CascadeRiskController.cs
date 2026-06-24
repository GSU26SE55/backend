using BatteryService.Application.CQRS.Command.CascadeRisk;
using BatteryService.Application.CQRS.Query.CascadeRisk;
using BatteryService.Application.DTOs;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharedContracts.Common.Responses;

namespace BatteryService.Api.Controllers;

/// <summary>
/// Sprint 7 B4 (§31.7) — <b>Cascade Risk Assessment</b> (rule-based propagation analysis).
/// </summary>
/// <remarks>
/// Đánh giá rủi ro 1 pin hỏng <b>lây lan</b> sang pin lân cận cùng site → ảnh hưởng Priority ticket.
///
/// Điểm rủi ro <c>CascadeRiskScore</c> (0.0–1.0) được tính lại định kỳ (mỗi 5 phút) bởi
/// <c>CascadeRiskBackgroundService</c> cho mọi asset có Open alert, theo 3 rule cộng dồn:
/// <list type="number">
///   <item><description><b>Topology factor</b>: SeriesString +0.6 · SeriesParallel +0.4 · ParallelBank +0.2 · Independent +0.0</description></item>
///   <item><description><b>Proximity</b>: ≥1 asset cùng Site có Open alert trong 1h +0.2 · ≥3 +0.2 (cộng dồn)</description></item>
///   <item><description><b>Thermal runaway</b>: asset có Overheat + Critical + Open +0.3</description></item>
/// </list>
/// Score được clamp ≤ 1.0. Khi cross ngưỡng <c>≥ 0.7</c> → publish <c>BatteryCascadeRiskHighEvent</c>
/// (TicketService upgrade Priority lên P1). Mức derive: <c>Low</c> &lt; 0.5 · <c>Medium</c> 0.5–&lt;0.7 · <c>High</c> ≥ 0.7.
///
/// <para><b>Adaptation:</b> project đã bỏ BatteryGroup → proximity nhóm theo <c>SiteId</c>.</para>
/// </remarks>
[ApiController]
[Produces("application/json")]
public class CascadeRiskController : ControllerBase
{
    private readonly IMediator _mediator;

    public CascadeRiskController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Cascade risk hiện tại của 1 asset (score 0.0–1.0 + level + electrical topology).</summary>
    /// <remarks>
    /// Trả <see cref="CascadeRiskDto"/>: <c>CascadeRiskScore</c>, <c>Level</c> (Low/Medium/High),
    /// <c>ElectricalTopology</c>, <c>CascadeRiskUpdatedAt</c> (lần recompute cuối).
    ///
    /// Trả về score đã lưu (background service giữ tươi mỗi 5 phút) — không recompute on-demand.
    /// Quyền: tất cả role đăng nhập (Customer dùng để xem rủi ro pin của mình).
    /// </remarks>
    /// <param name="id">Id của BatteryAsset.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa <see cref="CascadeRiskDto"/>.</returns>
    /// <response code="200">Trả cascade risk của asset.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role phù hợp.</response>
    /// <response code="404">Không tìm thấy asset (hoặc đã soft delete).</response>
    [HttpGet("api/battery-assets/{id:guid}/cascade-risk")]
    [Authorize(Roles = "Admin,Manager,Staff,Customer")]
    [ProducesResponseType(typeof(CommonResponse<CascadeRiskDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<CascadeRiskDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAssetCascadeRisk(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetBatteryAssetCascadeRiskQuery { Id = id }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Heat map cascade risk tổng hợp cho toàn bộ asset trong 1 site (Manager dashboard).</summary>
    /// <remarks>
    /// Trả <see cref="SiteCascadeRiskSummaryDto"/>: tổng asset, số High/Medium/Low, <c>MaxScore</c>,
    /// và danh sách <c>HighRiskAssets</c> (score ≥ 0.7, sort giảm dần) để hiển thị heat map / cảnh báo.
    ///
    /// Quyền: Admin/Manager.
    /// </remarks>
    /// <param name="id">Id của Site.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa <see cref="SiteCascadeRiskSummaryDto"/>.</returns>
    /// <response code="200">Trả heat map cascade risk của site.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role phù hợp.</response>
    /// <response code="404">Không tìm thấy site.</response>
    [HttpGet("api/sites/{id:guid}/cascade-risk-summary")]
    [Authorize(Roles = "Admin,Manager")]
    [ProducesResponseType(typeof(CommonResponse<SiteCascadeRiskSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<SiteCascadeRiskSummaryDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSiteCascadeRiskSummary(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetSiteCascadeRiskSummaryQuery { SiteId = id }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Admin set electrical topology cho asset (ảnh hưởng trực tiếp cascade risk score).</summary>
    /// <remarks>
    /// Body: <c>{ "electricalTopology": 1..4 }</c> — 1=Independent · 2=SeriesString · 3=ParallelBank · 4=SeriesParallel.
    /// <c>Id</c> lấy từ path (không nhận trong body).
    ///
    /// Sau khi set, score được recompute ngay để response phản ánh topology mới.
    /// Quyền: chỉ Admin.
    ///
    /// <para><b>Validation (field-level → <c>listErrors</c>):</b> <c>ElectricalTopology</c> ngoài 1..4 hoặc
    /// <c>Id</c> rỗng → 400 với <c>listErrors</c> chỉ rõ field. Lỗi không tìm thấy asset → 404 ở <c>message</c>.</para>
    /// </remarks>
    /// <param name="id">Id của BatteryAsset.</param>
    /// <param name="command">Body chứa <c>ElectricalTopology</c>.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa <see cref="CascadeRiskDto"/> sau khi cập nhật.</returns>
    /// <response code="200">Đã cập nhật topology + recompute score.</response>
    /// <response code="400">ElectricalTopology không hợp lệ (xem <c>listErrors</c>).</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không phải Admin.</response>
    /// <response code="404">Không tìm thấy asset.</response>
    [HttpPost("api/battery-assets/{id:guid}/topology")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(CommonResponse<CascadeRiskDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<CascadeRiskDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<CascadeRiskDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetTopology(
        Guid id, [FromBody] SetBatteryAssetTopologyCommand command, CancellationToken cancellationToken)
    {
        command.Id = id;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}
