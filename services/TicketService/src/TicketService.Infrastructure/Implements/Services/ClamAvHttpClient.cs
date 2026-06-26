using Microsoft.Extensions.Logging;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Enums;

namespace TicketService.Infrastructure.Implements.Services;

/// <summary>
/// Gọi ClamAV REST /scan endpoint (multipart POST).
/// Phản hồi dạng text: "Everything OK" → Clean; chứa "FOUND" → Infected; mọi lỗi → Failed.
/// </summary>
public class ClamAvHttpClient : IClamAvClient
{
    private readonly HttpClient _http;
    private readonly ILogger<ClamAvHttpClient> _logger;

    public ClamAvHttpClient(HttpClient http, ILogger<ClamAvHttpClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<VirusScanStatusEnum> ScanAsync(Stream fileStream, string fileName, CancellationToken ct)
    {
        try
        {
            using var content = new MultipartFormDataContent();
            using var streamContent = new StreamContent(fileStream);
            content.Add(streamContent, "file", fileName);

            var response = await _http.PostAsync("/scan", content, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (body.Contains("FOUND", StringComparison.OrdinalIgnoreCase))
                return VirusScanStatusEnum.Infected;

            if (body.Contains("OK", StringComparison.OrdinalIgnoreCase))
                return VirusScanStatusEnum.Clean;

            _logger.LogWarning("ClamAV unexpected response for {File}: {Body}", fileName, body);
            return VirusScanStatusEnum.Failed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ClamAV scan failed for file {File}", fileName);
            return VirusScanStatusEnum.Failed;
        }
    }
}
