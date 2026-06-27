using System.Security.Claims;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Realtime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedContracts.Common.Responses;
using SharedInfrastructure.Middleware;

namespace BatteryService.Api.Controllers;

/// <summary>
/// Sprint BE-IoT-Realtime (#614/#617) — SSE telemetry live stream (overall.md §34.10).
/// <c>GET /api/sensor-readings/stream?scope=asset|customer|site:{id}</c>.
/// Số đo ĐÃ làm sạch (publish sau ingest). Auth qua JWT — token có thể truyền <c>?access_token=</c>
/// (EventSource không set header; xử lý ở SharedInfrastructure JWT <c>OnMessageReceived</c>).
/// </summary>
[ApiController]
[Route("api/sensor-readings")]
public class SensorTelemetryStreamController : ControllerBase
{
    private readonly ITelemetryStream _stream;
    private readonly IBatteryRealtimeAuthorizationService _authz;

    public SensorTelemetryStreamController(ITelemetryStream stream, IBatteryRealtimeAuthorizationService authz)
    {
        _stream = stream;
        _authz = authz;
    }

    [HttpGet("stream")]
    [Authorize(Roles = "Admin,Manager,Staff,Customer")]
    [Produces("text/event-stream")]
    public async Task Stream([FromQuery] string scope, CancellationToken cancellationToken)
    {
        // Lỗi non-2xx trả CommonResponse (đồng nhất với /latest /history /aggregate):
        // - field-level (scope sai) → ListErrors {Field, Detail}, Message tổng quát.
        // - lỗi khác (auth) → chỉ Message, ListErrors null (ErrorsListJsonConverter tự null-hoá list rỗng).
        var parsed = TelemetryScope.Parse(scope);
        if (parsed is null)
        {
            await CommonResponseWriter.WriteAsync(
                Response, StatusCodes.Status400BadRequest, "Dữ liệu không hợp lệ.",
                new[]
                {
                    new Errors
                    {
                        Field = "scope",
                        Detail = "scope không hợp lệ. Dùng: asset:{id} | assets:{id1,id2} | customer:{id} | "
                               + "site:{id} | sites:{id1,id2} | type:{id} | all | site:none (mỗi list ≤ 50 id)."
                    }
                });
            return;
        }

        if (!TryGetUserId(out var actorUserId))
        {
            await CommonResponseWriter.WriteAsync(
                Response, StatusCodes.Status401Unauthorized, "Không xác định được người dùng.");
            return;
        }

        var roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        if (!await _authz.CanAccessScopeAsync(parsed.Value, actorUserId, roles, cancellationToken))
        {
            await CommonResponseWriter.WriteAsync(
                Response, StatusCodes.Status403Forbidden, "Không có quyền với scope này.");
            return;
        }

        // ─── Mở SSE ───
        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no"; // tắt buffering ở reverse proxy
        await Response.Body.FlushAsync(cancellationToken);

        // RequestAborted = hủy khi client đóng kết nối → stream tự unsubscribe Redis.
        var clientToken = HttpContext.RequestAborted;
        try
        {
            await foreach (var msg in _stream.SubscribeAsync(parsed.Value, clientToken))
            {
                await Response.WriteAsync($"event: {msg.Event}\n", clientToken);
                await Response.WriteAsync($"data: {msg.Data}\n\n", clientToken);
                await Response.Body.FlushAsync(clientToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnect — bình thường.
        }
    }

    private bool TryGetUserId(out Guid userId)
    {
        var raw = User.FindFirst("UserId")?.Value
                  ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? User.FindFirst("AccountId")?.Value;
        return Guid.TryParse(raw, out userId);
    }
}
