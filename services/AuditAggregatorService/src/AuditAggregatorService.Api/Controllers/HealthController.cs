using Microsoft.AspNetCore.Mvc;

namespace AuditAggregatorService.Api.Controllers;

/// <summary>
/// Health/liveness/readiness probe cho k8s (Sprint audit <c>#AUDIT-18</c>).
/// REST API audit (search/correlation/timeline/stats/export/replay) ở <c>#AUDIT-17</c>.
/// </summary>
[ApiController]
[Route("")]
[ApiExplorerSettings(GroupName = "public")]   // Probe hạ tầng (non-admin, không auth) — tách khỏi nhóm admin.
public class HealthController : ControllerBase
{
    /// <summary>Liveness probe — process còn sống hay không.</summary>
    /// <remarks>**Tác dụng:** k8s dùng để quyết định restart pod. **Actor:** hạ tầng (k8s/monitoring), không cần auth.</remarks>
    /// <response code="200">Process còn sống (<c>{ status: "alive" }</c>).</response>
    [HttpGet("live")]
    public IActionResult Live() => Ok(new { status = "alive" });

    /// <summary>Readiness probe — sẵn sàng nhận traffic chưa.</summary>
    /// <remarks>**Tác dụng:** k8s dùng để quyết định có route traffic vào pod. **Actor:** hạ tầng, không cần auth.</remarks>
    /// <response code="200">Sẵn sàng (<c>{ status: "ready" }</c>).</response>
    [HttpGet("ready")]
    public IActionResult Ready() => Ok(new { status = "ready" });

    /// <summary>Health tổng quát của service.</summary>
    /// <remarks>**Tác dụng:** kiểm tra nhanh tình trạng service. **Actor:** hạ tầng/monitoring, không cần auth.</remarks>
    /// <response code="200">OK (<c>{ status: "ok", service: "AuditAggregatorService" }</c>).</response>
    [HttpGet("health")]
    public IActionResult Health() => Ok(new { status = "ok", service = "AuditAggregatorService" });
}
