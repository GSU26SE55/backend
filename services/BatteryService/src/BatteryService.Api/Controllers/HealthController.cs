using Microsoft.AspNetCore.Mvc;

namespace BatteryService.Api.Controllers;

/// <summary>
/// Endpoint health check liveness cho BatteryService.
/// </summary>
/// <remarks>
/// Dùng cho:
/// <list type="bullet">
///   <item><description>Docker / Kubernetes liveness probe.</description></item>
///   <item><description>ApiGateway aggregated health UI.</description></item>
///   <item><description>Monitoring uptime của service.</description description></item>
/// </list>
/// Endpoint anonymous - không yêu cầu auth, không return dữ liệu nhạy cảm.
/// </remarks>
[ApiController]
[Route("api/battery/health")]
[Produces("application/json")]
public class HealthController : ControllerBase
{
    /// <summary>
    /// Trả về trạng thái Healthy + tên service + timestamp UTC hiện tại.
    /// </summary>
    /// <remarks>
    /// Đây là liveness check đơn giản: nếu request đến được endpoint và app đang chạy → trả 200.
    /// KHÔNG check DB / RabbitMQ / Redis - nếu cần readiness probe đầy đủ, dùng <c>/health/ready</c> sẽ được thêm Sprint 7.
    ///
    /// Response body:
    /// <code>
    /// {
    ///   "status": "Healthy",
    ///   "service": "BatteryService",
    ///   "timestamp": "2026-05-14T13:45:00.000Z"
    /// }
    /// </code>
    /// </remarks>
    /// <returns>JSON với 3 field <c>status</c>, <c>service</c>, <c>timestamp</c>.</returns>
    /// <response code="200">Service đang chạy.</response>
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
