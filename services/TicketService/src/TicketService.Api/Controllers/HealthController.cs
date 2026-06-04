using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TicketService.Application.Interfaces.Repositories;

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
}
