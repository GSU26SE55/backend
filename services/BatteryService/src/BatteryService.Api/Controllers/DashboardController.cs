using BatteryService.Application.CQRS.Query.Dashboard;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BatteryService.Api.Controllers;

/// <summary>
/// Sprint 5B B11 — Endpoint thống kê tổng quan cho Battery dashboard.
/// </summary>
[ApiController]
[Route("api/battery/dashboard")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly IMediator _mediator;

    public DashboardController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Thống kê tổng quan Battery dashboard (total/active/offline assets, open alerts breakdown, env incidents, asset status distribution, SOH distribution) — UI homepage Admin/Manager.
    /// </summary>
    /// <remarks>
    /// Trả các chỉ số tổng hợp dùng cho dashboard Admin/Manager:
    /// <list type="bullet">
    ///   <item><description>Tổng số <see cref="BatteryService.Domain.Entities.BatteryAsset"/> theo <c>Status</c> (Active / Inactive / Failed).</description></item>
    ///   <item><description>Số <see cref="BatteryService.Domain.Entities.Alert"/> Critical / Warning đang Open.</description></item>
    ///   <item><description>Phân phối SOH bucket (≥90 / 80–89 / 75–79 / &lt;75 → EOL).</description></item>
    ///   <item><description>Số reading bất thường trong 24h gần nhất.</description></item>
    /// </list>
    ///
    /// Cách hoạt động:
    /// - Nếu <c>siteId</c> được truyền, mọi count đều filter theo site đó.
    /// - Nếu không, trả tổng hợp toàn bộ system (yêu cầu role Admin/Manager).
    /// - Filter <c>!IsDeleted</c> trên tất cả truy vấn.
    ///
    /// Phân quyền: yêu cầu JWT hợp lệ (<c>[Authorize]</c> ở controller).
    /// </remarks>
    /// <param name="siteId">Tùy chọn — giới hạn thống kê trong 1 site cụ thể.</param>
    /// <returns><c>CommonResponse</c> chứa <c>BatteryDashboardStatsDto</c>.</returns>
    /// <response code="200">Trả thống kê thành công.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    [HttpGet("stats")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Stats([FromQuery] Guid? siteId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetBatteryDashboardStatsQuery { SiteId = siteId }, ct);
        return StatusCode(result.StatusCode, result);
    }
}
