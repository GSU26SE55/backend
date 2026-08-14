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
                Response, StatusCodes.Status400BadRequest, "Invalid data.",
                new[]
                {
                    new Errors
                    {
                        Field = "scope",
                        Detail = "Invalid scope. Use: asset:{id} | assets:{id1,id2} | customer:{id} | "
                               + "site:{id} | sites:{id1,id2} | type:{id} | all | site:none (each list ≤ 50 id)."
                    }
                });
            return;
        }

        if (!TryGetUserId(out var actorUserId))
        {
            await CommonResponseWriter.WriteAsync(
                Response, StatusCodes.Status401Unauthorized, "Unable to determine the user.");
            return;
        }

        var roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        if (!await _authz.CanAccessScopeAsync(parsed.Value, actorUserId, roles, cancellationToken))
        {
            await CommonResponseWriter.WriteAsync(
                Response, StatusCodes.Status403Forbidden, "You do not have permission for this scope.");
            return;
        }

        // ─── Mở SSE ───
        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no"; // tắt buffering ở reverse proxy
        await Response.Body.FlushAsync(cancellationToken);

        // ─── Last-Event-ID resume (#614) ───
        // `EventSource` tự nối lại khi rớt mạng và TỰ gửi header này kèm id cuối nó đã nhận — client
        // không phải viết thêm dòng code nào. Server đọc để biết phát bù từ đâu.
        // Header có thể xuất hiện dạng `Last-Event-ID` hoặc `Last-Event-Id` tuỳ client; HeaderDictionary
        // so sánh không phân biệt hoa thường nên 1 lần đọc là đủ.
        var lastEventId = Request.Headers["Last-Event-ID"].FirstOrDefault();

        // RequestAborted = hủy khi client đóng kết nối → stream tự unsubscribe Redis.
        var clientToken = HttpContext.RequestAborted;
        try
        {
            await foreach (var msg in _stream.SubscribeAsync(parsed.Value, lastEventId, clientToken))
            {
                // `id:` PHẢI đứng trước `data:` trong cùng khối event thì trình duyệt mới ghi nhận.
                // Chỉ ghi khi stream cấp id — không bịa id cho event mà server không phát lại được
                // (xem chú thích trên SseMessage.Id).
                if (!string.IsNullOrEmpty(msg.Id))
                    await Response.WriteAsync($"id: {msg.Id}\n", clientToken);

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
