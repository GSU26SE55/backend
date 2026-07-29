using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Infrastructure.Implements.Services;

/// <summary>
/// Gọi BatteryService GET /api/battery-assets/{id} lấy serial number của pin.
/// Propagate JWT của request hiện tại (Customer có quyền xem pin của mình) qua IHttpContextAccessor.
/// Fail/exception → trả null (KHÔNG throw, không chặn tạo ticket).
/// </summary>
public class BatteryLookupHttpClient : IBatteryLookupClient
{
    private readonly HttpClient _http;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<BatteryLookupHttpClient> _logger;

    public BatteryLookupHttpClient(
        HttpClient http,
        IHttpContextAccessor httpContextAccessor,
        ILogger<BatteryLookupHttpClient> logger)
    {
        _http = http;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task<string?> GetSerialAsync(Guid assetId, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/battery-assets/{assetId}");

            // Forward Authorization header từ request hiện tại để BatteryService authz.
            var authHeader = _httpContextAccessor.HttpContext?.Request.Headers["Authorization"].ToString();
            if (!string.IsNullOrWhiteSpace(authHeader))
                request.Headers.TryAddWithoutValidation("Authorization", authHeader);

            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Battery lookup {AssetId} returned {Status} — serial snapshot bỏ trống.",
                    assetId, (int)response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null)
                return null;

            if (data.TryGetProperty("serialNumber", out var serialProp) && serialProp.ValueKind == JsonValueKind.String)
            {
                var serial = serialProp.GetString();
                return string.IsNullOrWhiteSpace(serial) ? null : serial;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Battery lookup {AssetId} thất bại — serial snapshot bỏ trống.", assetId);
            return null;
        }
    }
}
