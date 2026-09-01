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

    /// Lookup hỏng KHÔNG được chặn việc tạo ticket — mọi nhánh lỗi trả về snapshot rỗng.
    private static readonly BatteryLookupResult Empty = new(null, null);

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
        => (await GetSnapshotAsync(assetId, ct)).SerialNumber;

    public async Task<BatteryLookupResult> GetSnapshotAsync(Guid assetId, CancellationToken ct)
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
                _logger.LogWarning("Battery lookup {AssetId} returned {Status} — snapshot bỏ trống.",
                    assetId, (int)response.StatusCode);
                return Empty;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null)
                return Empty;

            string? serial = null;
            if (data.TryGetProperty("serialNumber", out var serialProp) && serialProp.ValueKind == JsonValueKind.String)
            {
                var value = serialProp.GetString();
                serial = string.IsNullOrWhiteSpace(value) ? null : value;
            }

            Guid? siteId = null;
            if (data.TryGetProperty("siteId", out var siteProp)
                && siteProp.ValueKind == JsonValueKind.String
                && Guid.TryParse(siteProp.GetString(), out var parsedSite))
            {
                siteId = parsedSite;
            }

            return new BatteryLookupResult(serial, siteId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Battery lookup {AssetId} thất bại — snapshot bỏ trống.", assetId);
            return Empty;
        }
    }
}
