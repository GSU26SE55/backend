using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TicketService.Application.Common.Models;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Infrastructure.Implements.Services;

/// <summary>
/// Gọi FileStorageService REST API để upload audio file.
/// Forward Authorization header từ original request để FileStorageService auth user (#567).
/// FilePurpose=2 (TicketAttachment) — FileStorageService cho phép tất cả authenticated user.
/// </summary>
public class FileStorageApiClient : IFileUploadClient
{
    private const int FilePurposeTicketAttachment = 2; // FileStorageService FilePurposeEnum.TicketAttachment

    private readonly HttpClient _http;
    private readonly ChatOptions _opts;

    public FileStorageApiClient(HttpClient http, IOptions<ChatOptions> opts)
    {
        _http = http;
        _opts = opts.Value;
    }

    public async Task<(Guid FileId, string DownloadUrl)> UploadAsync(
        Stream fileStream,
        string fileName,
        string contentType,
        long sizeBytes,
        string authorizationHeader,
        CancellationToken ct = default)
    {
        fileStream.Seek(0, SeekOrigin.Begin);

        using var form = new MultipartFormDataContent();

        var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(streamContent, "file", fileName);
        form.Add(new StringContent("ticket-voice-audio"), "folderName");
        form.Add(new StringContent(FilePurposeTicketAttachment.ToString()), "purpose");

        using var request = new HttpRequestMessage(HttpMethod.Post, _opts.Voice.FileStorageUploadUrl);
        request.Content = form;
        if (!string.IsNullOrWhiteSpace(authorizationHeader))
            request.Headers.TryAddWithoutValidation("Authorization", authorizationHeader);

        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.GetProperty("data");

        var fileId = data.GetProperty("fileId").GetGuid();
        var downloadUrl = data.TryGetProperty("publicUrl", out var urlProp)
            ? urlProp.GetString() ?? string.Empty
            : string.Empty;

        return (fileId, downloadUrl);
    }
}
