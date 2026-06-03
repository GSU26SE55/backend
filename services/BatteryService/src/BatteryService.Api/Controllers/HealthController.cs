using Microsoft.AspNetCore.Mvc;

namespace BatteryService.Api.Controllers;

/// <summary>
/// Liveness health check endpoint for BatteryService.
/// </summary>
/// <remarks>
/// Used for:
/// <list type="bullet">
///   <item><description>Docker or Kubernetes liveness probes.</description></item>
///   <item><description>ApiGateway aggregated health UI.</description></item>
///   <item><description>Service uptime monitoring.</description></item>
/// </list>
/// This endpoint is anonymous and does not return sensitive data.
/// </remarks>
[ApiController]
[Route("api/battery/health")]
[Produces("application/json")]
public class HealthController : ControllerBase
{
    /// <summary>
    /// Returns Healthy status, service name, and current UTC timestamp.
    /// </summary>
    /// <remarks>
    /// This is a simple liveness check. If the request reaches the endpoint and the app is running,
    /// it returns 200. It does not validate database, RabbitMQ, or Redis connectivity.
    /// </remarks>
    /// <returns>JSON payload with <c>status</c>, <c>service</c>, and <c>timestamp</c>.</returns>
    /// <response code="200">Service is running.</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Get()
    {
        return Ok(new
        {
            status = "Healthy",
            service = "BatteryService",
            timestamp = DateTime.UtcNow
        });
    }
}
