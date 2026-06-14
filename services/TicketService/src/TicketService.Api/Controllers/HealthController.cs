using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Api.Controllers;

/// <summary>
/// Health probe + sync lag monitoring cho TicketService.
/// Dùng cho Kubernetes liveness/readiness probe + observability dashboard.
/// </summary>
[ApiController]
[Route("api/ticket/health")]
[Produces("application/json")]
public class HealthController : ControllerBase
{
    /// <summary>
    /// Liveness probe — chỉ trả 200 OK + metadata service (KHÔNG touch DB/cache/bus). Dùng cho K8s liveness probe periodSeconds=10 detect process còn alive.
    /// </summary>
    /// <remarks>
    /// Endpoint nhẹ nhất, không touch DB/cache/bus. Dùng cho K8s liveness probe
    /// (default <c>periodSeconds=10</c>) để detect process còn alive.
    /// Response shape: <c>{ status, service, timestamp }</c> — KHÔNG dùng CommonResponse vì
    /// monitoring tooling expect simple JSON.
    /// </remarks>
    /// <response code="200">Service alive.</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Get()
    {
        return Ok(new
        {
            status = "Healthy",
            service = "TicketService",
            timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Đo lag đồng bộ dữ liệu Customer/Staff từ AuthService qua MassTransit consumer.
    /// </summary>
    /// <remarks>
    /// Trả lag tính bằng giây giữa <c>UtcNow</c> và <c>LastSyncedAt</c> mới nhất trong
    /// <c>CustomerAccounts</c> + <c>StaffAccounts</c> tables.
    ///
    /// Threshold:
    /// <list type="bullet">
    ///   <item><description><c>status = "Healthy"</c>: maxLag ≤ 60s</description></item>
    ///   <item><description><c>status = "Warning"</c>: maxLag &gt; 60s (consumer chậm hoặc RabbitMQ backlog)</description></item>
    /// </list>
    ///
    /// Nếu table rỗng (chưa có sync nào), lag default = 365 ngày (báo Warning ngay).
    /// </remarks>
    /// <param name="uow">Unit of work injected (DI).</param>
    /// <response code="200">Trả lag metrics — luôn 200 (Warning là trạng thái dữ liệu, không phải fail HTTP).</response>
    [HttpGet("sync-lag")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSyncLag([FromServices] ITicketUnitOfWork uow)
    {
        var now = DateTime.UtcNow;

        var lastCustomerSync = await uow.CustomerAccounts.GetAllAsync()
            .OrderByDescending(c => c.LastSyncedAt)
            .Select(c => c.LastSyncedAt)
            .FirstOrDefaultAsync();

        var lastStaffSync = await uow.StaffAccounts.GetAllAsync()
            .OrderByDescending(s => s.LastSyncedAt)
            .Select(s => s.LastSyncedAt)
            .FirstOrDefaultAsync();

        var customerLag = lastCustomerSync == default ? TimeSpan.FromDays(365) : now - lastCustomerSync;
        var staffLag = lastStaffSync == default ? TimeSpan.FromDays(365) : now - lastStaffSync;

        var maxLagSeconds = Math.Max(customerLag.TotalSeconds, staffLag.TotalSeconds);

        return Ok(new
        {
            status = maxLagSeconds > 60 ? "Warning" : "Healthy",
            customerLagSeconds = customerLag.TotalSeconds,
            staffLagSeconds = staffLag.TotalSeconds,
            maxLagSeconds,
            timestamp = now
        });
    }

    /// <summary>
    /// Sprint 5B #239 — Alert–Ticket Saga health endpoint cho admin dashboard + Prometheus scrape.
    /// </summary>
    /// <remarks>
    /// Đếm 2 counter quan trọng trong 24h gần nhất:
    /// <list type="bullet">
    ///   <item><description><c>failedLast24h</c>: số saga state <c>Failed</c>.</description></item>
    ///   <item><description><c>stuckOver15min</c>: số saga state <c>TicketRequested</c> &gt; 15 phút (consumer chậm / RabbitMQ block / TicketService crash).</description></item>
    /// </list>
    ///
    /// Status derivation:
    /// <list type="bullet">
    ///   <item><description><c>Degraded</c>: failed &gt; 20 hoặc stuck &gt; 50 — page on-call.</description></item>
    ///   <item><description><c>Warning</c>: failed &gt; 5 hoặc stuck &gt; 10 — đáng để investigate.</description></item>
    ///   <item><description><c>Healthy</c>: dưới threshold trên.</description></item>
    /// </list>
    ///
    /// Endpoint chỉ trả counter — chi tiết từng saga query qua
    /// <c>GET /api/admin/sagas/alert-ticket?isFailed=true</c>.
    /// </remarks>
    /// <param name="sagaQuery">Saga query service injected (DI).</param>
    /// <response code="200">Trả health snapshot — luôn 200 (Degraded là trạng thái dữ liệu, không phải fail HTTP).</response>
    [HttpGet("saga")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSagaHealth([FromServices] IAlertTicketSagaQueryService sagaQuery)
    {
        // Health derived từ counters — chi tiết metrics ở Prometheus /metrics endpoint.
        var (failedPage, failedTotal) = await sagaQuery.QueryAsync(
            state: "Failed", alertId: null, batteryAssetId: null, customerId: null,
            startedFrom: DateTime.UtcNow.AddHours(-24), startedTo: null,
            isFailed: true, pageNumber: 1, pageSize: 1, isDescending: true, default);

        var (stuckPage, stuckTotal) = await sagaQuery.QueryAsync(
            state: "TicketRequested", alertId: null, batteryAssetId: null, customerId: null,
            startedFrom: null, startedTo: DateTime.UtcNow.AddMinutes(-15),
            isFailed: false, pageNumber: 1, pageSize: 1, isDescending: true, default);

        var status = failedTotal > 20 || stuckTotal > 50 ? "Degraded"
                    : failedTotal > 5 || stuckTotal > 10 ? "Warning"
                    : "Healthy";

        return Ok(new
        {
            status,
            failedLast24h = failedTotal,
            stuckOver15min = stuckTotal,
            timestamp = DateTime.UtcNow
        });
    }
}
