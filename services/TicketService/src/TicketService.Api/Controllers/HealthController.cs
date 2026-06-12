using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Api.Controllers;

[ApiController]
[Route("api/ticket/health")]
[Produces("application/json")]
public class HealthController : ControllerBase
{
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

    [HttpGet("sync-lag")]
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
    /// Sprint 5B #239 — /health/saga endpoint cho Alert–Ticket Saga monitoring.
    /// Trả các counter cho admin dashboard + Prometheus scrape.
    /// </summary>
    [HttpGet("saga")]
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
