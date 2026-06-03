using Microsoft.AspNetCore.Mvc;

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
}
