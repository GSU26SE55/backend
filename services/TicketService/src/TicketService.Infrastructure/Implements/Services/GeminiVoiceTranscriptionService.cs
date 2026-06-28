using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TicketService.Application.Common.Models;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Infrastructure.Implements.Services;

/// <summary>
/// Transcribe audio bằng Gemini 1.5 Flash multimodal (inline base64, max 20MB).
/// API key dùng chung với GeminiChatTextAiClient từ Chat:Ai:ApiKey (#567).
/// </summary>
public class GeminiVoiceTranscriptionService : IVoiceTranscriptionService
{
    private readonly HttpClient _http;
    private readonly ChatOptions _opts;

    public GeminiVoiceTranscriptionService(HttpClient http, IOptions<ChatOptions> opts)
    {
        _http = http;
        _opts = opts.Value;
    }

    public async Task<string> TranscribeAsync(Stream audioStream, string mimeType, CancellationToken ct = default)
    {
        var apiKey = _opts.Ai.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Gemini API key is not configured.");

        audioStream.Seek(0, SeekOrigin.Begin);
        var audioBytes = await ReadAllBytesAsync(audioStream, ct);
        var base64Audio = Convert.ToBase64String(audioBytes);

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_opts.Voice.ModelName}:generateContent?key={apiKey}";
        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new { inline_data = new { mime_type = mimeType, data = base64Audio } },
                        new { text = "Transcribe this audio message exactly as spoken. Return only the transcribed text without any explanation, formatting, or additional commentary." }
                    }
                }
            }
        };

        using var response = await _http.PostAsJsonAsync(url, requestBody, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            throw new InvalidOperationException("RATE_LIMITED");

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return ExtractText(json).Trim();
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        return ms.ToArray();
    }

    private static string ExtractText(JsonElement json)
    {
        if (!json.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            throw new InvalidOperationException("Gemini returned no candidates — response may have been blocked or quota exceeded.");

        return candidates[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString() ?? string.Empty;
    }
}
